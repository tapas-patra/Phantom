using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Phantom.Dashboard.Backend.Infrastructure;

namespace Phantom.Dashboard.Backend.Services;

public sealed class ManagedAiAdminService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly DashboardOptions _options;

    public ManagedAiAdminService(DashboardOptions options)
    {
        _options = options;
    }

    public async Task<object?> GetCredentialInventory(string authorizationHeader, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var credentials = await SendAsync<object>(HttpMethod.Get, "/api/admin/managed-ai/credentials", authorizationHeader, null, cancellationToken);
        var catalogs = await SendAsync<object>(HttpMethod.Get, "/api/admin/managed-ai/catalog", authorizationHeader, null, cancellationToken);
        return new
        {
            managedProviders = new[]
            {
                new { providerId = "ChatGPT", label = "ChatGPT", lane = "managed" },
                new { providerId = "Claude", label = "Claude", lane = "managed" },
                new { providerId = "Gemini", label = "Gemini", lane = "managed" },
                new { providerId = "Mistral", label = "Mistral", lane = "managed" },
                new { providerId = "Groq", label = "Groq", lane = "managed" },
                new { providerId = "NVIDIA", label = "NVIDIA", lane = "managed" }
            },
            credentials,
            catalogs
        };
    }

    public Task<object?> UpsertCredential(string authorizationHeader, object payload, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return SendAsync<object>(HttpMethod.Post, "/api/admin/managed-ai/credentials", authorizationHeader, payload, cancellationToken);
    }

    public Task<object?> RefreshCatalog(string authorizationHeader, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return SendAsync<object>(HttpMethod.Post, "/api/admin/managed-ai/catalog/refresh", authorizationHeader, null, cancellationToken);
    }

    public async Task DeleteCredential(string authorizationHeader, string credentialId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await SendAsync<object>(HttpMethod.Delete, $"/api/admin/managed-ai/credentials/{Uri.EscapeDataString(credentialId)}", authorizationHeader, null, cancellationToken);
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, string authorizationHeader, object? payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"{_options.WindowsBackendBaseUrl}{path}");
        if (!string.IsNullOrWhiteSpace(authorizationHeader))
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
        }
        else if (!string.IsNullOrWhiteSpace(_options.WindowsBackendAdminApiKey))
        {
            request.Headers.Add("X-Phantom-Admin-Key", _options.WindowsBackendAdminApiKey);
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
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(body)
                ? $"Managed AI admin request failed: {(int)response.StatusCode}"
                : body);
        }

        if (typeof(T) == typeof(object))
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private void EnsureConfigured()
    {
        if (!_options.HasWindowsBackendAdminAccess)
        {
            throw new InvalidOperationException(
                "Configure PHANTOM_WINDOWS_BACKEND_BASE_URL for managed AI admin controls.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
