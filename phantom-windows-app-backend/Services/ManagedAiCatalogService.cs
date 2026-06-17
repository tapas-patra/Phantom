using System.Net.Http.Headers;
using System.Text.Json;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class ManagedAiCatalogService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(12);
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly ManagedProviderCredentialRepository _credentials;
    private readonly ManagedProviderCatalogRepository _catalogRepository;
    private readonly SecretProtector _protector;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public ManagedAiCatalogService(
        ManagedProviderCredentialRepository credentials,
        ManagedProviderCatalogRepository catalogRepository,
        SecretProtector protector)
    {
        _credentials = credentials;
        _catalogRepository = catalogRepository;
        _protector = protector;
    }

    public ManagedAiCatalogDto GetCatalogForAccount(DesktopAccountRecord account)
    {
        EnsureCatalogFreshAsync().GetAwaiter().GetResult();
        var configuredProviderIds = _credentials.ListAll()
            .Where(item => item.IsEnabled)
            .Select(item => item.ProviderId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var providers = _catalogRepository.ListAll()
            .Where(item => configuredProviderIds.Contains(item.ProviderId))
            .Select(MapProvider)
            .Where(item => item.Models.Count > 0)
            .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ManagedAiCatalogDto
        {
            Providers = providers,
            RefreshedAtUtc = providers.Length == 0
                ? DateTime.UtcNow
                : providers.Max(item => item.RefreshedAtUtc)
        };
    }

    public bool IsAllowedModel(string provider, string model)
    {
        EnsureCatalogFreshAsync().GetAwaiter().GetResult();
        var record = _catalogRepository.FindByProviderId(provider);
        if (record == null)
        {
            return false;
        }

        return DeserializeModels(record.ModelsJson)
            .Any(item => string.Equals(item.ModelId, model, StringComparison.OrdinalIgnoreCase));
    }

    public async Task RefreshConfiguredProvidersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCatalogFreshAsync(force: true, cancellationToken);
    }

    private async Task EnsureCatalogFreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var credentialsByProvider = _credentials.ListAll()
                .Where(item => item.IsEnabled && ManagedAiCatalog.IsAllowedProvider(item.ProviderId))
                .GroupBy(item => item.ProviderId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(item => item.Priority).ThenByDescending(item => item.UpdatedAtUtc).First(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var providerId in ManagedAiCatalog.GetAllProviders())
            {
                if (!credentialsByProvider.TryGetValue(providerId, out var credential))
                {
                    continue;
                }

                var existing = _catalogRepository.FindByProviderId(providerId);
                var isStale = existing == null || existing.RefreshedAtUtc <= DateTime.UtcNow - RefreshInterval;
                if (!force && !isStale)
                {
                    continue;
                }

                var apiKey = _protector.Unprotect(credential.EncryptedApiKey);
                var models = await FetchModelsForProviderAsync(providerId, apiKey, cancellationToken);
                if (models.Count == 0)
                {
                    continue;
                }

                _catalogRepository.Save(new ManagedProviderCatalogRecord
                {
                    ProviderId = providerId,
                    Label = ManagedAiCatalog.GetProviderLabel(providerId),
                    ModelsJson = JsonSerializer.Serialize(models),
                    RefreshedAtUtc = DateTime.UtcNow
                });
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static ManagedAiProviderOptionDto MapProvider(ManagedProviderCatalogRecord record)
    {
        return new ManagedAiProviderOptionDto
        {
            ProviderId = record.ProviderId,
            Label = record.Label,
            Models = DeserializeModels(record.ModelsJson),
            RefreshedAtUtc = record.RefreshedAtUtc
        };
    }

    private static IReadOnlyList<ManagedAiModelOptionDto> DeserializeModels(string modelsJson)
    {
        return JsonSerializer.Deserialize<List<ManagedAiModelOptionDto>>(modelsJson)
            ?.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? Array.Empty<ManagedAiModelOptionDto>();
    }

    private static async Task<IReadOnlyList<ManagedAiModelOptionDto>> FetchModelsForProviderAsync(
        string providerId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        return providerId switch
        {
            var p when string.Equals(p, ManagedAiCatalog.ChatGpt, StringComparison.OrdinalIgnoreCase)
                => await FetchOpenAiModelsAsync("https://api.openai.com/v1/models", apiKey, cancellationToken),
            var p when string.Equals(p, ManagedAiCatalog.Claude, StringComparison.OrdinalIgnoreCase)
                => await FetchAnthropicModelsAsync(apiKey, cancellationToken),
            var p when string.Equals(p, ManagedAiCatalog.Gemini, StringComparison.OrdinalIgnoreCase)
                => await FetchGeminiModelsAsync(apiKey, cancellationToken),
            var p when string.Equals(p, ManagedAiCatalog.Mistral, StringComparison.OrdinalIgnoreCase)
                => await FetchOpenAiModelsAsync("https://api.mistral.ai/v1/models", apiKey, cancellationToken),
            var p when string.Equals(p, ManagedAiCatalog.Nvidia, StringComparison.OrdinalIgnoreCase)
                => await FetchOpenAiModelsAsync("https://integrate.api.nvidia.com/v1/models", apiKey, cancellationToken),
            _ => Array.Empty<ManagedAiModelOptionDto>()
        };
    }

    private static async Task<IReadOnlyList<ManagedAiModelOptionDto>> FetchOpenAiModelsAsync(
        string url,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ManagedAiModelOptionDto>();
        }

        var models = new List<ManagedAiModelOptionDto>();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idElement))
            {
                continue;
            }

            var id = idElement.GetString();
            if (string.IsNullOrWhiteSpace(id) || !LooksLikeChatModel(id))
            {
                continue;
            }

            models.Add(new ManagedAiModelOptionDto
            {
                ModelId = id,
                DisplayName = id,
                SupportsVision = InferVisionSupport(id, null)
            });
        }

        return models
            .DistinctBy(item => item.ModelId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<ManagedAiModelOptionDto>> FetchAnthropicModelsAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ManagedAiModelOptionDto>();
        }

        var models = new List<ManagedAiModelOptionDto>();
        foreach (var item in data.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var displayName = item.TryGetProperty("display_name", out var displayNameElement)
                ? displayNameElement.GetString()
                : id;

            models.Add(new ManagedAiModelOptionDto
            {
                ModelId = id,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName!,
                SupportsVision = InferVisionSupport(id, displayName)
            });
        }

        return models
            .DistinctBy(item => item.ModelId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<ManagedAiModelOptionDto>> FetchGeminiModelsAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(
            $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(apiKey)}",
            cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("models", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ManagedAiModelOptionDto>();
        }

        var models = new List<ManagedAiModelOptionDto>();
        foreach (var item in data.EnumerateArray())
        {
            var baseModelId = item.TryGetProperty("baseModelId", out var baseModelIdElement)
                ? baseModelIdElement.GetString()
                : null;
            var supportedMethods = item.TryGetProperty("supportedGenerationMethods", out var methodsElement) && methodsElement.ValueKind == JsonValueKind.Array
                ? methodsElement.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
                : Array.Empty<string?>();

            if (string.IsNullOrWhiteSpace(baseModelId)
                || !supportedMethods.Any(method => string.Equals(method, "generateContent", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var displayName = item.TryGetProperty("displayName", out var displayNameElement)
                ? displayNameElement.GetString()
                : baseModelId;

            models.Add(new ManagedAiModelOptionDto
            {
                ModelId = baseModelId,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? baseModelId : displayName!,
                SupportsVision = InferVisionSupport(baseModelId, displayName)
            });
        }

        return models
            .DistinctBy(item => item.ModelId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool LooksLikeChatModel(string modelId)
    {
        var normalized = modelId.ToLowerInvariant();
        if (normalized.Contains("embedding")
            || normalized.Contains("moderation")
            || normalized.Contains("whisper")
            || normalized.Contains("tts")
            || normalized.Contains("transcribe")
            || normalized.Contains("image")
            || normalized.Contains("rerank"))
        {
            return false;
        }

        return normalized.Contains("gpt")
            || normalized.StartsWith("o1")
            || normalized.StartsWith("o3")
            || normalized.Contains("claude")
            || normalized.Contains("mistral")
            || normalized.Contains("mixtral")
            || normalized.Contains("pixtral")
            || normalized.Contains("gemini")
            || normalized.Contains("nemotron")
            || normalized.Contains("llama")
            || normalized.Contains("qwen")
            || normalized.Contains("gemma")
            || normalized.Contains("deepseek")
            || normalized.Contains("kimi")
            || normalized.Contains("glm")
            || normalized.Contains("nvidia");
    }

    private static bool InferVisionSupport(string? modelId, string? displayName)
    {
        var normalized = $"{modelId} {displayName}".ToLowerInvariant();
        return normalized.Contains("vision")
            || normalized.Contains("4o")
            || normalized.Contains("omni")
            || normalized.Contains("claude")
            || normalized.Contains("gemini")
            || normalized.Contains("pixtral")
            || normalized.Contains("vlm")
            || normalized.Contains("image")
            || normalized.Contains("multimodal");
    }
}
