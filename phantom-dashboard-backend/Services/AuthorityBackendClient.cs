using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Phantom.Dashboard.Backend.Infrastructure;

namespace Phantom.Dashboard.Backend.Services;

public sealed class AuthorityBackendClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly DashboardOptions _options;
    private readonly object _sync = new();
    private int _consecutiveFailures;
    private DateTime? _lastSuccessAtUtc;
    private DateTime? _lastFailureAtUtc;
    private DateTime? _circuitOpenUntilUtc;

    public AuthorityBackendClient(DashboardOptions options)
    {
        _options = options;
    }

    public async Task<T?> SendAsync<T>(
        HttpMethod method,
        string path,
        string authorizationHeader,
        object? payload,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        ThrowIfCircuitOpen();

        var delay = TimeSpan.FromMilliseconds(200);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, $"{_options.WindowsBackendBaseUrl}{path}");
                if (!string.IsNullOrWhiteSpace(authorizationHeader))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
                }

                if (!string.IsNullOrWhiteSpace(_options.WindowsBackendInternalApiKey))
                {
                    request.Headers.Add("X-Phantom-Internal-Key", _options.WindowsBackendInternalApiKey);
                }

                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (payload != null)
                {
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(payload),
                        Encoding.UTF8,
                        "application/json");
                }

                using var response = await HttpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    RecordSuccess();
                    if (typeof(T) == typeof(object))
                    {
                        return JsonSerializer.Deserialize<T>(body, JsonOptions);
                    }

                    if (typeof(T) == typeof(string))
                    {
                        return (T?)(object?)body;
                    }

                    return string.IsNullOrWhiteSpace(body)
                        ? default
                        : JsonSerializer.Deserialize<T>(body, JsonOptions);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized
                    || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    RecordSuccess();
                    throw new UnauthorizedAccessException(string.IsNullOrWhiteSpace(body)
                        ? "Authority backend rejected the request."
                        : body);
                }

                if (response.StatusCode == HttpStatusCode.BadRequest
                    || response.StatusCode == HttpStatusCode.NotFound)
                {
                    RecordSuccess();
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(body)
                        ? $"Authority backend request failed: {(int)response.StatusCode}"
                        : body);
                }

                if (attempt == 3)
                {
                    RecordFailure();
                    throw new HttpRequestException(string.IsNullOrWhiteSpace(body)
                        ? $"Authority backend request failed: {(int)response.StatusCode}"
                        : body);
                }
            }
            catch (Exception ex) when (attempt < 3 && IsTransient(ex, cancellationToken))
            {
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                RecordFailure();
                throw;
            }
        }

        return default;
    }

    public object GetHealthSnapshot()
    {
        lock (_sync)
        {
            return new
            {
                configured = _options.HasWindowsBackendInternalAccess,
                consecutiveFailures = _consecutiveFailures,
                lastSuccessAtUtc = _lastSuccessAtUtc,
                lastFailureAtUtc = _lastFailureAtUtc,
                circuitOpenUntilUtc = _circuitOpenUntilUtc
            };
        }
    }

    public bool IsReady()
    {
        lock (_sync)
        {
            if (!_options.HasWindowsBackendInternalAccess)
            {
                return false;
            }

            return !_circuitOpenUntilUtc.HasValue || _circuitOpenUntilUtc.Value <= DateTime.UtcNow;
        }
    }

    private void EnsureConfigured()
    {
        if (!_options.HasWindowsBackendInternalAccess)
        {
            throw new InvalidOperationException(
                "Configure PHANTOM_WINDOWS_BACKEND_BASE_URL and PHANTOM_WINDOWS_BACKEND_INTERNAL_API_KEY.");
        }
    }

    private void ThrowIfCircuitOpen()
    {
        lock (_sync)
        {
            if (_circuitOpenUntilUtc.HasValue && _circuitOpenUntilUtc.Value > DateTime.UtcNow)
            {
                throw new HttpRequestException("Authority backend circuit is open.");
            }

            if (_circuitOpenUntilUtc.HasValue && _circuitOpenUntilUtc.Value <= DateTime.UtcNow)
            {
                _circuitOpenUntilUtc = null;
            }
        }
    }

    private void RecordSuccess()
    {
        lock (_sync)
        {
            _consecutiveFailures = 0;
            _lastSuccessAtUtc = DateTime.UtcNow;
            _circuitOpenUntilUtc = null;
        }
    }

    private void RecordFailure()
    {
        lock (_sync)
        {
            _consecutiveFailures++;
            _lastFailureAtUtc = DateTime.UtcNow;
            if (_consecutiveFailures >= 5)
            {
                _circuitOpenUntilUtc = DateTime.UtcNow.AddSeconds(30);
            }
        }
    }

    private static bool IsTransient(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return ex is HttpRequestException or TaskCanceledException;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
