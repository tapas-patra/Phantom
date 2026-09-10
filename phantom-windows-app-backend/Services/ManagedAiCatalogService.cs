using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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
    private readonly ManagedAiRuntimeSelectionRepository _runtimeSelectionRepository;
    private readonly SecretProtector _protector;
    private readonly ILogger<ManagedAiCatalogService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public ManagedAiCatalogService(
        ManagedProviderCredentialRepository credentials,
        ManagedProviderCatalogRepository catalogRepository,
        ManagedAiRuntimeSelectionRepository runtimeSelectionRepository,
        SecretProtector protector,
        ILogger<ManagedAiCatalogService> logger)
    {
        _credentials = credentials;
        _catalogRepository = catalogRepository;
        _runtimeSelectionRepository = runtimeSelectionRepository;
        _protector = protector;
        _logger = logger;
    }

    public ManagedAiCatalogDto GetCatalogForAccount(DesktopAccountRecord account)
    {
        var configuredProviderIds = _credentials.ListAll()
            .Where(item => item.IsEnabled)
            .Select(item => item.ProviderId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var providers = _catalogRepository.ListAll()
            .Where(item => configuredProviderIds.Contains(item.ProviderId))
            .Select(MapProviderChatEligible)
            .Where(item => item.Models.Count > 0)
            .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var runtimeSelection = GetResolvedRuntimeSelection(providers);
        if (runtimeSelection != null)
        {
            providers = providers
                .Where(item => string.Equals(item.ProviderId, runtimeSelection.ProviderId, StringComparison.OrdinalIgnoreCase))
                .Select(item => new ManagedAiProviderOptionDto
                {
                    ProviderId = item.ProviderId,
                    Label = item.Label,
                    RefreshedAtUtc = item.RefreshedAtUtc,
                    Models = item.Models
                        .Where(model => string.Equals(model.ModelId, runtimeSelection.ModelId, StringComparison.OrdinalIgnoreCase))
                        .ToArray()
                })
                .Where(item => item.Models.Count > 0)
                .ToArray();
        }
        else if (providers.Length > 0)
        {
            var fallbackProvider = providers[0];
            var fallbackModel = fallbackProvider.Models.FirstOrDefault();
            if (fallbackModel != null)
            {
                providers = new[]
                {
                    new ManagedAiProviderOptionDto
                    {
                        ProviderId = fallbackProvider.ProviderId,
                        Label = fallbackProvider.Label,
                        RefreshedAtUtc = fallbackProvider.RefreshedAtUtc,
                        Models = new[] { fallbackModel }
                    }
                };
            }
        }

        return new ManagedAiCatalogDto
        {
            Providers = providers,
            RefreshedAtUtc = providers.Length == 0
                ? DateTime.UtcNow
                : providers.Max(item => item.RefreshedAtUtc)
        };
    }

    public ManagedAiCatalogDto GetByoCatalog(DesktopAccountRecord account)
    {
        RequireByoAccess(account);
        return BuildByoCatalogDto();
    }

    public async Task<ManagedAiCatalogDto> RefreshByoCatalogAsync(
        DesktopAccountRecord account,
        ByoModelCatalogRequestDto request,
        CancellationToken cancellationToken)
    {
        RequireByoAccess(account);
        var providerId = request.ProviderId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(providerId) && !ManagedAiCatalog.IsAllowedProvider(providerId))
        {
            throw new BackendValidationException("Unsupported BYO provider.");
        }

        try
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                await RefreshCatalogAsync(force: true, cancellationToken);
            }
            else
            {
                await RefreshSingleProviderCatalogAsync(providerId, cancellationToken);
            }

            return BuildByoCatalogDto();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BackendValidationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "BYO AI catalog refresh failed for provider {ProviderId}; error_type={ErrorType}",
                string.IsNullOrWhiteSpace(providerId) ? "all" : providerId,
                ex.GetType().Name);
            throw new BackendValidationException("Unable to refresh BYO model catalogs. Please retry.");
        }
    }

    private ManagedAiCatalogDto BuildByoCatalogDto()
    {
        // BYO users call providers with their own keys. Serve the admin catalog
        // (synced + manually added models) regardless of whether Phantom has a
        // managed credential for that provider.
        var catalogByProvider = _catalogRepository.ListAll()
            .ToDictionary(item => item.ProviderId, StringComparer.OrdinalIgnoreCase);

        var providers = ManagedAiCatalog.GetAllProviders()
            .Select(providerId =>
            {
                if (catalogByProvider.TryGetValue(providerId, out var record))
                {
                    return MapProviderChatEligible(record);
                }

                return new ManagedAiProviderOptionDto
                {
                    ProviderId = providerId,
                    Label = ManagedAiCatalog.GetProviderLabel(providerId),
                    Models = Array.Empty<ManagedAiModelOptionDto>(),
                    // Never emit DateTime.MinValue — macOS ISO-8601 decoding rejects 0001-01-01.
                    RefreshedAtUtc = DateTime.UtcNow
                };
            })
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

    private async Task RefreshSingleProviderCatalogAsync(string providerId, CancellationToken cancellationToken)
    {
        var credential = _credentials.ListByProvider(providerId)
            .Where(item => item.IsEnabled)
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefault()
            ?? throw new BackendValidationException($"No managed credential is configured for {ManagedAiCatalog.GetProviderLabel(providerId)}.");

        var existing = _catalogRepository.FindByProviderId(providerId);
        var existingModels = existing == null
            ? Array.Empty<ManagedAiModelOptionDto>()
            : DeserializeModels(existing.ModelsJson);

        var apiKey = _protector.Unprotect(credential.EncryptedApiKey);
        var fetched = await FetchModelsForProviderAsync(providerId, apiKey, cancellationToken);
        var models = MergeFetchedModelsWithExisting(fetched, existingModels);
        var refreshedAtUtc = DateTime.UtcNow;
        _catalogRepository.Save(new ManagedProviderCatalogRecord
        {
            ProviderId = providerId,
            Label = ManagedAiCatalog.GetProviderLabel(providerId),
            ModelsJson = JsonSerializer.Serialize(models),
            RefreshedAtUtc = refreshedAtUtc
        });

        _logger.LogInformation(
            "BYO AI catalog refreshed for provider {ProviderId}; model_count={ModelCount}",
            providerId,
            models.Count);
    }

    public IReadOnlyList<ManagedAiProviderOptionDto> ListCatalogProviders()
    {
        return _catalogRepository.ListAll()
            .Select(MapProvider)
            .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ManagedAiRuntimeSelectionDto GetAdminRuntimeSelection()
    {
        return MapRuntimeSelection(_runtimeSelectionRepository.Get(), ListCatalogProviders());
    }

    public ManagedAiRuntimeSelectionDto UpdateAdminRuntimeSelection(ManagedAiRuntimeSelectionUpdateRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderId))
        {
            throw new BackendValidationException("ProviderId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            throw new BackendValidationException("ModelId is required.");
        }

        var providers = ListCatalogProviders();
        var provider = providers.FirstOrDefault(item => string.Equals(item.ProviderId, request.ProviderId, StringComparison.OrdinalIgnoreCase))
            ?? throw new BackendValidationException("Managed provider catalog not found.");
        var model = provider.Models.FirstOrDefault(item => string.Equals(item.ModelId, request.ModelId, StringComparison.OrdinalIgnoreCase))
            ?? throw new BackendValidationException("Managed model not found.");

        if (!model.EligibleForChat)
        {
            throw new BackendValidationException("Active premium model must be eligible for chat.");
        }

        if (!_credentials.ListByProvider(provider.ProviderId).Any(item => item.IsEnabled))
        {
            throw new BackendValidationException("At least one enabled credential is required for the selected provider.");
        }

        var record = new ManagedAiRuntimeSelectionRecord
        {
            SelectionId = ManagedAiRuntimeSelectionRepository.GlobalSelectionId,
            ProviderId = provider.ProviderId,
            ModelId = model.ModelId,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _runtimeSelectionRepository.Save(record);
        return MapRuntimeSelection(record, providers);
    }

    public bool IsAllowedModel(string provider, string model)
    {
        var record = _catalogRepository.FindByProviderId(provider);
        if (record == null)
        {
            return false;
        }

        return DeserializeModels(record.ModelsJson)
            .Any(item =>
                string.Equals(item.ModelId, model, StringComparison.OrdinalIgnoreCase)
                && item.EligibleForChat);
    }

    public bool ModelSupportsVision(string provider, string model)
    {
        var record = _catalogRepository.FindByProviderId(provider);
        if (record == null)
        {
            return false;
        }

        return DeserializeModels(record.ModelsJson)
            .FirstOrDefault(item => string.Equals(item.ModelId, model, StringComparison.OrdinalIgnoreCase))
            ?.SupportsVision == true;
    }

    public ManagedAiProviderOptionDto UpdateModelVisionSupport(ManagedAiModelVisionUpdateRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderId))
        {
            throw new BackendValidationException("ProviderId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            throw new BackendValidationException("ModelId is required.");
        }

        var record = _catalogRepository.FindByProviderId(request.ProviderId)
            ?? throw new BackendValidationException("Managed provider catalog not found.");
        var models = DeserializeModels(record.ModelsJson).ToList();
        var model = models.FirstOrDefault(item => string.Equals(item.ModelId, request.ModelId, StringComparison.OrdinalIgnoreCase))
            ?? throw new BackendValidationException("Managed model not found.");

        model.SupportsVision = request.SupportsVision;
        record.ModelsJson = JsonSerializer.Serialize(models);
        record.RefreshedAtUtc = DateTime.UtcNow;
        _catalogRepository.Save(record);

        return MapProvider(record);
    }

    public ManagedAiProviderOptionDto UpdateModelFlags(ManagedAiModelFlagsUpdateRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderId))
        {
            throw new BackendValidationException("ProviderId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            throw new BackendValidationException("ModelId is required.");
        }

        if (request.SupportsVision == null && request.EligibleForChat == null)
        {
            throw new BackendValidationException("At least one of SupportsVision or EligibleForChat is required.");
        }

        var record = _catalogRepository.FindByProviderId(request.ProviderId)
            ?? throw new BackendValidationException("Managed provider catalog not found.");
        var models = DeserializeModels(record.ModelsJson).ToList();
        var model = models.FirstOrDefault(item => string.Equals(item.ModelId, request.ModelId, StringComparison.OrdinalIgnoreCase))
            ?? throw new BackendValidationException("Managed model not found.");

        if (request.SupportsVision.HasValue)
        {
            model.SupportsVision = request.SupportsVision.Value;
        }

        if (request.EligibleForChat.HasValue)
        {
            model.EligibleForChat = request.EligibleForChat.Value;
        }

        record.ModelsJson = JsonSerializer.Serialize(models);
        record.RefreshedAtUtc = DateTime.UtcNow;
        _catalogRepository.Save(record);

        return MapProvider(record);
    }

    public ManagedAiProviderOptionDto AddOrUpdateModel(ManagedAiModelUpsertRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderId))
        {
            throw new BackendValidationException("ProviderId is required.");
        }

        if (!ManagedAiCatalog.IsAllowedProvider(request.ProviderId))
        {
            throw new BackendValidationException("Unsupported managed AI provider.");
        }

        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            throw new BackendValidationException("ModelId is required.");
        }

        var providerId = ManagedAiCatalog.GetAllProviders()
            .First(item => string.Equals(item, request.ProviderId, StringComparison.OrdinalIgnoreCase));
        var modelId = request.ModelId.Trim();
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? modelId : request.DisplayName.Trim();

        var record = _catalogRepository.FindByProviderId(providerId);
        var models = record == null
            ? new List<ManagedAiModelOptionDto>()
            : DeserializeModels(record.ModelsJson).ToList();

        var model = models.FirstOrDefault(item => string.Equals(item.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
        if (model == null)
        {
            model = new ManagedAiModelOptionDto { ModelId = modelId };
            models.Add(model);
        }

        model.ModelId = modelId;
        model.DisplayName = displayName;
        model.SupportsVision = request.SupportsVision;
        model.EligibleForChat = request.EligibleForChat;

        record ??= new ManagedProviderCatalogRecord
        {
            ProviderId = providerId,
            Label = ManagedAiCatalog.GetProviderLabel(providerId)
        };

        record.Label = ManagedAiCatalog.GetProviderLabel(providerId);
        record.ModelsJson = JsonSerializer.Serialize(
            models.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray());
        record.RefreshedAtUtc = DateTime.UtcNow;
        _catalogRepository.Save(record);

        return MapProvider(record);
    }

    public async Task<ManagedAiCatalogRefreshResultDto> RefreshConfiguredProvidersAsync(CancellationToken cancellationToken = default)
    {
        var providers = await RefreshCatalogAsync(force: true, cancellationToken);
        return new ManagedAiCatalogRefreshResultDto
        {
            RefreshedAtUtc = DateTime.UtcNow,
            Providers = providers
        };
    }

    private async Task<IReadOnlyList<ManagedAiCatalogRefreshProviderResultDto>> RefreshCatalogAsync(
        bool force,
        CancellationToken cancellationToken)
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

            var results = new List<ManagedAiCatalogRefreshProviderResultDto>();

            foreach (var providerId in ManagedAiCatalog.GetAllProviders())
            {
                var existing = _catalogRepository.FindByProviderId(providerId);
                var existingModels = existing == null
                    ? Array.Empty<ManagedAiModelOptionDto>()
                    : DeserializeModels(existing.ModelsJson);

                if (!credentialsByProvider.TryGetValue(providerId, out var credential))
                {
                    // No managed credential → cannot live-fetch, but keep catalog rows
                    // (including admin-manual models) so BYO clients still receive them.
                    results.Add(BuildRefreshResult(
                        providerId,
                        attempted: false,
                        succeeded: existingModels.Count > 0,
                        message: existingModels.Count > 0
                            ? "No managed credential; serving existing catalog models."
                            : "No enabled credential configured.",
                        models: existingModels,
                        refreshedAtUtc: existing?.RefreshedAtUtc));
                    continue;
                }

                var isStale = existing == null || existing.RefreshedAtUtc <= DateTime.UtcNow - RefreshInterval;
                if (!force && !isStale)
                {
                    results.Add(BuildRefreshResult(
                        providerId,
                        attempted: false,
                        succeeded: true,
                        message: "Catalog is still fresh.",
                        models: existingModels,
                        refreshedAtUtc: existing?.RefreshedAtUtc));
                    continue;
                }

                try
                {
                    var apiKey = _protector.Unprotect(credential.EncryptedApiKey);
                    var fetched = await FetchModelsForProviderAsync(providerId, apiKey, cancellationToken);
                    var models = MergeFetchedModelsWithExisting(fetched, existingModels);
                    var refreshedAtUtc = DateTime.UtcNow;

                    _catalogRepository.Save(new ManagedProviderCatalogRecord
                    {
                        ProviderId = providerId,
                        Label = ManagedAiCatalog.GetProviderLabel(providerId),
                        ModelsJson = JsonSerializer.Serialize(models),
                        RefreshedAtUtc = refreshedAtUtc
                    });

                    results.Add(BuildRefreshResult(
                        providerId,
                        attempted: true,
                        succeeded: true,
                        message: $"Fetched {models.Count} model(s).",
                        models: models,
                        refreshedAtUtc: refreshedAtUtc));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Managed AI catalog refresh failed for provider {ProviderId}. Serving cached catalog when available.",
                        providerId);

                    results.Add(BuildRefreshResult(
                        providerId,
                        attempted: true,
                        succeeded: false,
                        message: GetSingleLineMessage(ex),
                        models: existingModels,
                        refreshedAtUtc: existing?.RefreshedAtUtc));
                }
            }

            return results
                .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
                .ToArray();
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
            RefreshedAtUtc = NormalizeCatalogTimestamp(record.RefreshedAtUtc)
        };
    }

    private static DateTime NormalizeCatalogTimestamp(DateTime value)
    {
        // Swift clients reject DateTime.MinValue / year-0001 ISO strings.
        return value.Year < 2 ? DateTime.UtcNow : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private static ManagedAiProviderOptionDto MapProviderChatEligible(ManagedProviderCatalogRecord record)
    {
        var mapped = MapProvider(record);
        return new ManagedAiProviderOptionDto
        {
            ProviderId = mapped.ProviderId,
            Label = mapped.Label,
            RefreshedAtUtc = mapped.RefreshedAtUtc,
            Models = mapped.Models.Where(item => item.EligibleForChat).ToArray()
        };
    }

    private static IReadOnlyList<ManagedAiModelOptionDto> MergeFetchedModelsWithExisting(
        IReadOnlyList<ManagedAiModelOptionDto> fetched,
        IReadOnlyList<ManagedAiModelOptionDto> existing)
    {
        var existingById = existing.ToDictionary(item => item.ModelId, StringComparer.OrdinalIgnoreCase);
        return fetched
            .Select(fetchedModel =>
            {
                if (!existingById.TryGetValue(fetchedModel.ModelId, out var existingModel))
                {
                    return fetchedModel;
                }

                return new ManagedAiModelOptionDto
                {
                    ModelId = fetchedModel.ModelId,
                    DisplayName = string.IsNullOrWhiteSpace(fetchedModel.DisplayName)
                        ? existingModel.DisplayName
                        : fetchedModel.DisplayName,
                    SupportsVision = existingModel.SupportsVision,
                    EligibleForChat = existingModel.EligibleForChat
                };
            })
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<ManagedAiModelOptionDto> DeserializeModels(string modelsJson)
    {
        return JsonSerializer.Deserialize<List<ManagedAiModelOptionDto>>(modelsJson)
            ?.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? Array.Empty<ManagedAiModelOptionDto>();
    }

    private ManagedAiRuntimeSelectionRecord? GetResolvedRuntimeSelection(
        IReadOnlyList<ManagedAiProviderOptionDto> providers)
    {
        var selection = _runtimeSelectionRepository.Get();
        if (selection == null)
        {
            return null;
        }

        var provider = providers.FirstOrDefault(item => string.Equals(item.ProviderId, selection.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (provider == null)
        {
            return null;
        }

        return provider.Models.Any(item => string.Equals(item.ModelId, selection.ModelId, StringComparison.OrdinalIgnoreCase))
            ? selection
            : null;
    }

    private static ManagedAiRuntimeSelectionDto MapRuntimeSelection(
        ManagedAiRuntimeSelectionRecord? selection,
        IReadOnlyList<ManagedAiProviderOptionDto> providers)
    {
        if (selection == null)
        {
            return new ManagedAiRuntimeSelectionDto
            {
                IsConfigured = false,
                IsResolved = false
            };
        }

        var provider = providers.FirstOrDefault(item => string.Equals(item.ProviderId, selection.ProviderId, StringComparison.OrdinalIgnoreCase));
        var model = provider?.Models.FirstOrDefault(item => string.Equals(item.ModelId, selection.ModelId, StringComparison.OrdinalIgnoreCase));

        return new ManagedAiRuntimeSelectionDto
        {
            IsConfigured = true,
            IsResolved = provider != null && model != null,
            ProviderId = selection.ProviderId,
            ProviderLabel = provider?.Label ?? selection.ProviderId,
            ModelId = selection.ModelId,
            ModelDisplayName = model?.DisplayName ?? selection.ModelId,
            UpdatedAtUtc = selection.UpdatedAtUtc
        };
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
            var p when string.Equals(p, ManagedAiCatalog.Groq, StringComparison.OrdinalIgnoreCase)
                => await FetchOpenAiModelsAsync("https://api.groq.com/openai/v1/models", apiKey, cancellationToken),
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
            if (string.IsNullOrWhiteSpace(id) || !LooksLikeInventoryModel(id))
            {
                continue;
            }

            models.Add(new ManagedAiModelOptionDto
            {
                ModelId = id,
                DisplayName = id,
                SupportsVision = InferVisionSupport(id, null),
                EligibleForChat = LooksLikeChatModel(id)
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
                SupportsVision = InferVisionSupport(id, displayName),
                EligibleForChat = true
            });
        }

        return models
            .DistinctBy(item => item.ModelId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<ManagedAiModelOptionDto>> FetchGeminiModelsAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models");
        request.Headers.Add("x-goog-api-key", apiKey);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
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
                SupportsVision = InferVisionSupport(baseModelId, displayName),
                EligibleForChat = true
            });
        }

        return models
            .DistinctBy(item => item.ModelId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool LooksLikeInventoryModel(string modelId)
    {
        var normalized = modelId.ToLowerInvariant();
        return !(normalized.Contains("embedding")
            || normalized.Contains("moderation")
            || normalized.Contains("whisper")
            || normalized.Contains("tts")
            || normalized.Contains("transcribe")
            || normalized.Contains("image")
            || normalized.Contains("rerank")
            || normalized.Contains("audio")
            || normalized.Contains("realtime"));
    }

    private static bool LooksLikeChatModel(string modelId)
    {
        var normalized = modelId.ToLowerInvariant();
        if (!LooksLikeInventoryModel(modelId))
        {
            return false;
        }

        return normalized.Contains("gpt")
            || normalized.StartsWith("o1")
            || normalized.StartsWith("o3")
            || normalized.StartsWith("o4")
            || normalized.Contains("claude")
            || normalized.Contains("mistral")
            || normalized.Contains("mixtral")
            || normalized.Contains("pixtral")
            || normalized.Contains("gemini")
            || normalized.Contains("compound")
            || normalized.Contains("nemotron")
            || normalized.Contains("llama")
            || normalized.Contains("qwen")
            || normalized.Contains("gemma")
            || normalized.Contains("deepseek")
            || normalized.Contains("kimi")
            || normalized.Contains("glm")
            || normalized.Contains("instruct")
            || normalized.Contains("chat")
            || normalized.Contains("reasoning")
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
            || normalized.Contains("mistral-large")
            || normalized.Contains("pixtral")
            || normalized.Contains("vlm")
            || normalized.Contains("image")
            || normalized.Contains("multimodal")
            || normalized.Contains("llama-4")
            || normalized.Contains("scout")
            || normalized.Contains("maverick");
    }

    private static ManagedAiCatalogRefreshProviderResultDto BuildRefreshResult(
        string providerId,
        bool attempted,
        bool succeeded,
        string message,
        IReadOnlyList<ManagedAiModelOptionDto> models,
        DateTime? refreshedAtUtc)
    {
        return new ManagedAiCatalogRefreshProviderResultDto
        {
            ProviderId = providerId,
            Label = ManagedAiCatalog.GetProviderLabel(providerId),
            Attempted = attempted,
            Succeeded = succeeded,
            Message = message,
            ModelCount = models.Count,
            RefreshedAtUtc = refreshedAtUtc
        };
    }

    private static string GetSingleLineMessage(Exception ex)
    {
        var message = ex.Message?.Trim() ?? ex.GetType().Name;
        var newlineIndex = message.IndexOfAny(['\r', '\n']);
        return newlineIndex >= 0 ? message[..newlineIndex].Trim() : message;
    }

    private static void RequireByoAccess(DesktopAccountRecord account)
    {
        if (account.ProAvailableCredits > 0m)
        {
            return;
        }

        var effectiveTier = AccessModeResolver.GetEffectiveAccessTier(account);
        if (string.Equals(effectiveTier, AccessModeResolver.ProByo, StringComparison.OrdinalIgnoreCase)
            || string.Equals(account.AccessTier, AccessModeResolver.ProByo, StringComparison.OrdinalIgnoreCase)
            || account.CanUseDesktopPowerFeatures)
        {
            return;
        }

        throw new BackendValidationException("BYO model catalog access requires a Pro BYO entitlement.");
    }
}
