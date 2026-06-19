namespace Phantom.Dashboard.Backend.Services;

public sealed class ManagedAiAdminService
{
    private readonly AuthorityBackendClient _authority;

    public ManagedAiAdminService(AuthorityBackendClient authority)
    {
        _authority = authority;
    }

    public async Task<object?> GetCredentialInventory(string authorizationHeader, CancellationToken cancellationToken)
    {
        var credentials = await _authority.SendAsync<object>(HttpMethod.Get, "/api/admin/managed-ai/credentials", authorizationHeader, null, cancellationToken);
        var catalogs = await _authority.SendAsync<object>(HttpMethod.Get, "/api/admin/managed-ai/catalog", authorizationHeader, null, cancellationToken);
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

    public Task<object?> GetOverview(string authorizationHeader, CancellationToken cancellationToken)
    {
        return _authority.SendAsync<object>(HttpMethod.Get, "/api/admin/overview", authorizationHeader, null, cancellationToken);
    }

    public Task<object?> GetPaymentOrders(string authorizationHeader, int limit, CancellationToken cancellationToken)
    {
        return _authority.SendAsync<object>(
            HttpMethod.Get,
            $"/api/admin/payments/orders?limit={Math.Max(1, limit)}",
            authorizationHeader,
            null,
            cancellationToken);
    }

    public Task<object?> GetPaymentWebhookEvents(string authorizationHeader, int limit, CancellationToken cancellationToken)
    {
        return _authority.SendAsync<object>(
            HttpMethod.Get,
            $"/api/admin/payments/webhooks?limit={Math.Max(1, limit)}",
            authorizationHeader,
            null,
            cancellationToken);
    }

    public Task<object?> UpsertCredential(string authorizationHeader, object payload, CancellationToken cancellationToken)
    {
        return _authority.SendAsync<object>(HttpMethod.Post, "/api/admin/managed-ai/credentials", authorizationHeader, payload, cancellationToken);
    }

    public Task<object?> RefreshCatalog(string authorizationHeader, CancellationToken cancellationToken)
    {
        return _authority.SendAsync<object>(HttpMethod.Post, "/api/admin/managed-ai/catalog/refresh", authorizationHeader, null, cancellationToken);
    }

    public async Task DeleteCredential(string authorizationHeader, string credentialId, CancellationToken cancellationToken)
    {
        await _authority.SendAsync<object>(HttpMethod.Delete, $"/api/admin/managed-ai/credentials/{Uri.EscapeDataString(credentialId)}", authorizationHeader, null, cancellationToken);
    }
}
