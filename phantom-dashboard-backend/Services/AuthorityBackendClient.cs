using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Phantom.Dashboard.Backend.Infrastructure;

namespace Phantom.Dashboard.Backend.Services;

public sealed class AuthorityBackendClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly DashboardOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuthorityBackendClient> _logger;
    private readonly object _sync = new();
    private int _consecutiveFailures;
    private DateTime? _lastSuccessAtUtc;
    private DateTime? _lastFailureAtUtc;
    private DateTime? _circuitOpenUntilUtc;

    public AuthorityBackendClient(DashboardOptions options, IHttpContextAccessor httpContextAccessor, ILogger<AuthorityBackendClient> logger)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
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
            var started = Stopwatch.GetTimestamp();
            var route = SafeRoute(path);
            try
            {
                _logger.LogInformation(
                    "authority_request_started service={Service} component={Component} event={Event} method={Method} route_template={RouteTemplate} attempt={Attempt}",
                    "phantom-dashboard-backend", "authority_client", "outbound_request_started", method.Method, route, attempt);
                using var request = new HttpRequestMessage(method, $"{_options.WindowsBackendBaseUrl}{path}");
                if (!string.IsNullOrWhiteSpace(authorizationHeader))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
                }

                if (!string.IsNullOrWhiteSpace(_options.WindowsBackendInternalApiKey))
                {
                    request.Headers.Add("X-Phantom-Internal-Key", _options.WindowsBackendInternalApiKey);
                }

                var inbound = _httpContextAccessor.HttpContext?.Request.Headers;
                if (inbound != null)
                {
                    request.Headers.TryAddWithoutValidation("X-Phantom-Correlation-Id", inbound["X-Phantom-Correlation-Id"].FirstOrDefault());
                    request.Headers.TryAddWithoutValidation("X-Phantom-Operation-Id", inbound["X-Phantom-Operation-Id"].FirstOrDefault());
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
                _logger.LogInformation(
                    "authority_request_completed service={Service} component={Component} event={Event} method={Method} route_template={RouteTemplate} attempt={Attempt} status_class={StatusClass} elapsed_ms={ElapsedMs} outcome={Outcome}",
                    "phantom-dashboard-backend", "authority_client", "outbound_request_completed", method.Method, route, attempt,
                    $"{(int)response.StatusCode / 100}xx", Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    response.IsSuccessStatusCode ? "success" : "error");
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
                    throw new UnauthorizedAccessException("Authority backend rejected the request.");
                }

                if (response.StatusCode == HttpStatusCode.BadRequest
                    || response.StatusCode == HttpStatusCode.NotFound)
                {
                    RecordSuccess();
                    throw new InvalidOperationException($"Authority backend request failed: {(int)response.StatusCode}");
                }

                if (attempt == 3)
                {
                    RecordFailure();
                    throw new HttpRequestException($"Authority backend request failed: {(int)response.StatusCode}");
                }
            }
            catch (Exception ex) when (attempt < 3 && IsTransient(ex, cancellationToken))
            {
                _logger.LogWarning(
                    "authority_request_retry service={Service} component={Component} event={Event} method={Method} route_template={RouteTemplate} attempt={Attempt} elapsed_ms={ElapsedMs} error_code={ErrorCode}",
                    "phantom-dashboard-backend", "authority_client", "outbound_retry_started", method.Method, route, attempt,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds, ex.GetType().Name);
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
                _logger.LogError(
                    "authority_request_failed service={Service} component={Component} event={Event} method={Method} route_template={RouteTemplate} attempt={Attempt} elapsed_ms={ElapsedMs}",
                    "phantom-dashboard-backend", "authority_client", "outbound_request_failed", method.Method, route, attempt,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds);
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

    private static string SafeRoute(string path)
    {
        const string credentialPrefix = "/api/admin/managed-ai/credentials/";
        var queryIndex = path.IndexOf('?');
        var route = queryIndex >= 0 ? path[..queryIndex] : path;
        return route.StartsWith(credentialPrefix, StringComparison.Ordinal)
            ? credentialPrefix + "{credentialId}"
            : route;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
