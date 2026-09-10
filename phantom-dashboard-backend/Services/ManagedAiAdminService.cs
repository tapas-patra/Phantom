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
        var credentialsTask = _authority.SendAsync<object>(HttpMethod.Get, "/api/admin/managed-ai/credentials", authorizationHeader, null, cancellationToken);
        var catalogsTask = _authority.SendAsync<object>(HttpMethod.Get, "/api/admin/managed-ai/catalog", authorizationHeader, null, cancellationToken);
        var selectionTask = _authority.SendAsync<object>(HttpMethod.Get, "/api/admin/managed-ai/selection", authorizationHeader, null, cancellationToken);
        var kbEmbeddingTask = _authority.SendAsync<object>(HttpMethod.Get, "/api/admin/kb/embedding-config", authorizationHeader, null, cancellationToken);

        await Task.WhenAll(credentialsTask, catalogsTask, selectionTask, kbEmbeddingTask);

        var credentials = await credentialsTask;
        var catalogs = await catalogsTask;
        var selection = await selectionTask;
        var kbEmbedding = await kbEmbeddingTask;
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
            selection,
            kbEmbedding,
            credentials,
            catalogs
        };
    }

    public async Task<object?> GetSpeechCredentialInventory(string authorizationHeader, CancellationToken cancellationToken)
    {
        var credentialsTask = _authority.SendAsync<object>(HttpMethod.Get, "/api/admin/managed-speech/credentials", authorizationHeader, null, cancellationToken);
        var catalogsTask = _authority.SendAsync<object>(HttpMethod.Get, "/api/admin/managed-speech/catalog", authorizationHeader, null, cancellationToken);
        var selectionTask = _authority.SendAsync<object>(HttpMethod.Get, "/api/admin/managed-speech/selection", authorizationHeader, null, cancellationToken);
        await Task.WhenAll(credentialsTask, catalogsTask, selectionTask);
        return new
        {
            managedProviders = new[]
            {
                new { providerId = "ChatGPT", label = "OpenAI", lane = "speech" },
                new { providerId = "Groq", label = "Groq", lane = "speech" },
                new { providerId = "Mistral", label = "Mistral", lane = "speech" }
            },
            credentials = await credentialsTask,
            catalogs = await catalogsTask,
            selection = await selectionTask
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

    public Task<object?> UpsertSpeechCredential(string authorizationHeader, object payload, CancellationToken cancellationToken) =>
        _authority.SendAsync<object>(HttpMethod.Post, "/api/admin/managed-speech/credentials", authorizationHeader, payload, cancellationToken);

    public Task<object?> RefreshSpeechCatalog(string authorizationHeader, CancellationToken cancellationToken) =>
        _authority.SendAsync<object>(HttpMethod.Post, "/api/admin/managed-speech/catalog/refresh", authorizationHeader, null, cancellationToken);

    public Task<object?> UpdateSpeechRuntimeSelection(string authorizationHeader, object payload, CancellationToken cancellationToken) =>
        _authority.SendAsync<object>(HttpMethod.Post, "/api/admin/managed-speech/selection", authorizationHeader, payload, cancellationToken);

    public Task<object?> UpdateRuntimeSelection(string authorizationHeader, object payload, CancellationToken cancellationToken)
    {
        return _authority.SendAsync<object>(HttpMethod.Post, "/api/admin/managed-ai/selection", authorizationHeader, payload, cancellationToken);
    }

    public Task<object?> UpdateKnowledgeBaseEmbeddingConfig(string authorizationHeader, object payload, CancellationToken cancellationToken)
    {
        return _authority.SendAsync<object>(HttpMethod.Post, "/api/admin/kb/embedding-config", authorizationHeader, payload, cancellationToken);
    }

    public async Task DeleteCredential(string authorizationHeader, string credentialId, CancellationToken cancellationToken)
    {
        await _authority.SendAsync<object>(HttpMethod.Delete, $"/api/admin/managed-ai/credentials/{Uri.EscapeDataString(credentialId)}", authorizationHeader, null, cancellationToken);
    }

    public async Task DeleteSpeechCredential(string authorizationHeader, string credentialId, CancellationToken cancellationToken) =>
        await _authority.SendAsync<object>(HttpMethod.Delete, $"/api/admin/managed-speech/credentials/{Uri.EscapeDataString(credentialId)}", authorizationHeader, null, cancellationToken);
}
