using System.Net.Http.Headers;
using System.Text.Json;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class ManagedSpeechCatalogService
{
    public const string Workload = "speech";
    public const string SelectionId = "global-speech";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(12);
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly ManagedProviderCredentialRepository _credentials;
    private readonly ManagedSpeechCatalogRepository _catalogs;
    private readonly ManagedAiRuntimeSelectionRepository _selections;
    private readonly SecretProtector _protector;
    private readonly ILogger<ManagedSpeechCatalogService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public ManagedSpeechCatalogService(
        ManagedProviderCredentialRepository credentials,
        ManagedSpeechCatalogRepository catalogs,
        ManagedAiRuntimeSelectionRepository selections,
        SecretProtector protector,
        ILogger<ManagedSpeechCatalogService> logger)
    {
        _credentials = credentials;
        _catalogs = catalogs;
        _selections = selections;
        _protector = protector;
        _logger = logger;
    }

    public ManagedAiCatalogDto GetCatalogForAccount(DesktopAccountRecord account)
    {
        var tier = AccessModeResolver.GetEffectiveAccessTier(account);
        if (string.Equals(tier, AccessModeResolver.Free, StringComparison.OrdinalIgnoreCase))
            return new ManagedAiCatalogDto { RefreshedAtUtc = DateTime.UtcNow };

        var providers = ListCatalogProviders().Where(item => item.Models.Count > 0).ToArray();
        if (string.Equals(tier, AccessModeResolver.Premium, StringComparison.OrdinalIgnoreCase))
        {
            var selection = ResolveSelection(providers);
            providers = selection == null
                ? Array.Empty<ManagedAiProviderOptionDto>()
                : providers.Where(item => string.Equals(item.ProviderId, selection.ProviderId, StringComparison.OrdinalIgnoreCase))
                    .Select(item => new ManagedAiProviderOptionDto
                    {
                        ProviderId = item.ProviderId,
                        Label = item.Label,
                        RefreshedAtUtc = item.RefreshedAtUtc,
                        Models = item.Models.Where(model => string.Equals(model.ModelId, selection.ModelId, StringComparison.OrdinalIgnoreCase)).ToArray()
                    }).ToArray();
        }

        return new ManagedAiCatalogDto
        {
            Providers = providers,
            RefreshedAtUtc = providers.Length == 0 ? DateTime.UtcNow : providers.Max(item => item.RefreshedAtUtc)
        };
    }

    public IReadOnlyList<ManagedAiProviderOptionDto> ListCatalogProviders() =>
        _catalogs.ListAll().Select(MapProvider).OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase).ToArray();

    public ManagedAiRuntimeSelectionDto GetAdminRuntimeSelection() =>
        MapSelection(_selections.Get(SelectionId), ListCatalogProviders());

    public ManagedAiRuntimeSelectionDto UpdateAdminRuntimeSelection(ManagedAiRuntimeSelectionUpdateRequestDto request)
    {
        var provider = ListCatalogProviders().FirstOrDefault(item => string.Equals(item.ProviderId, request.ProviderId, StringComparison.OrdinalIgnoreCase))
            ?? throw new BackendValidationException("Speech provider catalog not found.");
        var model = provider.Models.FirstOrDefault(item => string.Equals(item.ModelId, request.ModelId, StringComparison.OrdinalIgnoreCase))
            ?? throw new BackendValidationException("Speech model not found.");
        if (!_credentials.ListByProvider(provider.ProviderId, Workload).Any(item => item.IsEnabled))
            throw new BackendValidationException("At least one enabled speech credential is required for the selected provider.");

        var record = new ManagedAiRuntimeSelectionRecord
        {
            SelectionId = SelectionId,
            ProviderId = provider.ProviderId,
            ModelId = model.ModelId,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _selections.Save(record);
        return MapSelection(record, ListCatalogProviders());
    }

    public ManagedAiRuntimeSelectionRecord RequireResolvedSelection()
    {
        var selection = ResolveSelection(ListCatalogProviders());
        return selection ?? throw new BackendValidationException("Managed speech recognition is not configured.");
    }

    public bool IsAllowedModel(string provider, string model) =>
        _catalogs.FindByProviderId(provider) is { } record
        && DeserializeModels(record.ModelsJson).Any(item => string.Equals(item.ModelId, model, StringComparison.OrdinalIgnoreCase));

    public async Task<ManagedAiCatalogRefreshResultDto> RefreshConfiguredProvidersAsync(CancellationToken cancellationToken = default)
        => await RefreshConfiguredProvidersAsync(force: false, cancellationToken);

    public async Task<ManagedAiCatalogRefreshResultDto> ForceRefreshAsync(CancellationToken cancellationToken = default)
        => await RefreshConfiguredProvidersAsync(force: true, cancellationToken);

    private async Task<ManagedAiCatalogRefreshResultDto> RefreshConfiguredProvidersAsync(bool force, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var credentials = _credentials.ListAll(Workload).Where(item => item.IsEnabled)
                .GroupBy(item => item.ProviderId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Priority).First(), StringComparer.OrdinalIgnoreCase);
            var results = new List<ManagedAiCatalogRefreshProviderResultDto>();

            foreach (var providerId in ManagedSpeechCatalog.GetAllProviders())
            {
                var existing = _catalogs.FindByProviderId(providerId);
                var existingModels = existing == null ? Array.Empty<ManagedAiModelOptionDto>() : DeserializeModels(existing.ModelsJson);
                if (!credentials.TryGetValue(providerId, out var credential))
                {
                    results.Add(Result(providerId, false, false, "No enabled speech credential configured.", existingModels.Count, existing?.RefreshedAtUtc));
                    continue;
                }
                if (!force && existing != null && existing.RefreshedAtUtc > DateTime.UtcNow - RefreshInterval)
                {
                    results.Add(Result(providerId, false, true, "Catalog is still fresh.", existingModels.Count, existing.RefreshedAtUtc));
                    continue;
                }
                try
                {
                    var models = await FetchModelsAsync(providerId, _protector.Unprotect(credential.EncryptedApiKey), cancellationToken);
                    if (models.Count == 0) throw new InvalidOperationException("Provider returned no supported speech models.");
                    var now = DateTime.UtcNow;
                    _catalogs.Save(new ManagedProviderCatalogRecord
                    {
                        ProviderId = providerId,
                        Label = ManagedSpeechCatalog.GetProviderLabel(providerId),
                        ModelsJson = JsonSerializer.Serialize(models),
                        RefreshedAtUtc = now
                    });
                    results.Add(Result(providerId, true, true, $"Fetched {models.Count} speech model(s).", models.Count, now));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Managed speech catalog refresh failed for {ProviderId}", providerId);
                    results.Add(Result(providerId, true, false, ex.Message.Split('\n')[0], existingModels.Count, existing?.RefreshedAtUtc));
                }
            }

            return new ManagedAiCatalogRefreshResultDto { RefreshedAtUtc = DateTime.UtcNow, Providers = results };
        }
        finally { _refreshLock.Release(); }
    }

    private static async Task<IReadOnlyList<ManagedAiModelOptionDto>> FetchModelsAsync(string provider, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ManagedSpeechCatalog.GetModelsUrl(provider));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Array.Empty<ManagedAiModelOptionDto>();

        return data.EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id) && ManagedSpeechCatalog.LooksLikeSpeechModel(provider, id!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Select(id => new ManagedAiModelOptionDto { ModelId = id!, DisplayName = id!, SupportsVision = false })
            .ToArray();
    }

    private ManagedAiRuntimeSelectionRecord? ResolveSelection(IReadOnlyList<ManagedAiProviderOptionDto> providers)
    {
        var selection = _selections.Get(SelectionId);
        var provider = selection == null ? null : providers.FirstOrDefault(item => string.Equals(item.ProviderId, selection.ProviderId, StringComparison.OrdinalIgnoreCase));
        return provider?.Models.Any(item => string.Equals(item.ModelId, selection!.ModelId, StringComparison.OrdinalIgnoreCase)) == true ? selection : null;
    }

    private static ManagedAiProviderOptionDto MapProvider(ManagedProviderCatalogRecord record) => new()
    {
        ProviderId = record.ProviderId,
        Label = record.Label,
        Models = DeserializeModels(record.ModelsJson),
        RefreshedAtUtc = record.RefreshedAtUtc
    };

    private static IReadOnlyList<ManagedAiModelOptionDto> DeserializeModels(string json) =>
        JsonSerializer.Deserialize<List<ManagedAiModelOptionDto>>(json)?.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray()
        ?? Array.Empty<ManagedAiModelOptionDto>();

    private static ManagedAiRuntimeSelectionDto MapSelection(ManagedAiRuntimeSelectionRecord? selection, IReadOnlyList<ManagedAiProviderOptionDto> providers)
    {
        var provider = selection == null ? null : providers.FirstOrDefault(item => string.Equals(item.ProviderId, selection.ProviderId, StringComparison.OrdinalIgnoreCase));
        var model = provider?.Models.FirstOrDefault(item => string.Equals(item.ModelId, selection?.ModelId, StringComparison.OrdinalIgnoreCase));
        return selection == null ? new ManagedAiRuntimeSelectionDto() : new ManagedAiRuntimeSelectionDto
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

    private static ManagedAiCatalogRefreshProviderResultDto Result(string provider, bool attempted, bool succeeded, string message, int count, DateTime? refreshed) => new()
    {
        ProviderId = provider,
        Label = ManagedSpeechCatalog.GetProviderLabel(provider),
        Attempted = attempted,
        Succeeded = succeeded,
        Message = message,
        ModelCount = count,
        RefreshedAtUtc = refreshed
    };
}
