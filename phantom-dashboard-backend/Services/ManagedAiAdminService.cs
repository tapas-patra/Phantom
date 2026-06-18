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

    public async Task<object?> GetCredentialInventory(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var credentials = await SendAsync<object>(HttpMethod.Get, "/api/admin/managed-ai/credentials", null, cancellationToken);
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
            credentials
        };
    }

    public Task<object?> UpsertCredential(object payload, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return SendAsync<object>(HttpMethod.Post, "/api/admin/managed-ai/credentials", payload, cancellationToken);
    }

    public Task<object?> RefreshCatalog(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return SendAsync<object>(HttpMethod.Post, "/api/admin/managed-ai/catalog/refresh", null, cancellationToken);
    }

    public async Task DeleteCredential(string credentialId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await SendAsync<object>(HttpMethod.Delete, $"/api/admin/managed-ai/credentials/{Uri.EscapeDataString(credentialId)}", null, cancellationToken);
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"{_options.WindowsBackendBaseUrl}{path}");
        request.Headers.Add("X-Phantom-Admin-Key", _options.WindowsBackendAdminApiKey);
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
                "Configure PHANTOM_WINDOWS_BACKEND_BASE_URL and PHANTOM_WINDOWS_BACKEND_ADMIN_API_KEY for managed AI admin controls.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
