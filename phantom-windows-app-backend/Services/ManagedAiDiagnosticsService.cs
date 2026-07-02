using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class ManagedAiDiagnosticsService
{
    private static readonly IReadOnlyList<DesktopAiChatMessageDto> ProbeMessages = new[]
    {
        new DesktopAiChatMessageDto
        {
            Role = "user",
            Content = "Reply with only OK."
        }
    };

    private readonly ManagedAiLatencyRepository _latencyRepository;
    private readonly ManagedAiCatalogService _catalogService;
    private readonly ManagedAiService _managedAi;
    private readonly ILogger<ManagedAiDiagnosticsService> _logger;

    public ManagedAiDiagnosticsService(
        ManagedAiLatencyRepository latencyRepository,
        ManagedAiCatalogService catalogService,
        ManagedAiService managedAi,
        ILogger<ManagedAiDiagnosticsService> logger)
    {
        _latencyRepository = latencyRepository;
        _catalogService = catalogService;
        _managedAi = managedAi;
        _logger = logger;
    }

    public ManagedAiLatencyStatusDto GetLatencyStatus()
    {
        var latestRun = _latencyRepository.GetLatestRun();
        var statusByModel = _latencyRepository.ListModelStatuses()
            .ToDictionary(
                item => BuildKey(item.ProviderId, item.ModelId),
                item => item,
                StringComparer.OrdinalIgnoreCase);

        var models = _catalogService.ListCatalogProviders()
            .SelectMany(provider => provider.Models.Select(model => MapModelStatus(
                provider,
                model,
                statusByModel.TryGetValue(BuildKey(provider.ProviderId, model.ModelId), out var status) ? status : null)))
            .OrderBy(item => item.ProviderLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ManagedAiLatencyStatusDto
        {
            LatestRun = latestRun == null ? null : MapRun(latestRun),
            Models = models
        };
    }

    public ManagedAiLatencyRunDto EnqueueLatencyCheck()
    {
        var catalogProviders = _catalogService.ListCatalogProviders();
        var totalModels = catalogProviders.Sum(provider => provider.Models.Count);
        if (totalModels == 0)
        {
            throw new BackendValidationException("No fetched managed models are available yet. Refresh models first.");
        }

        var activeRun = _latencyRepository.FindActiveRun();
        if (activeRun != null)
        {
            return MapRun(activeRun);
        }

        var now = DateTime.UtcNow;
        var run = new ManagedAiLatencyRunRecord
        {
            JobId = $"managed-latency-{Guid.NewGuid():N}",
            Status = "queued",
            TotalModels = totalModels,
            ProcessedModels = 0,
            Error = string.Empty,
            RequestedAtUtc = now,
            UpdatedAtUtc = now
        };

        if (!_latencyRepository.Enqueue(run))
        {
            return MapRun(_latencyRepository.FindActiveRun() ?? run);
        }

        return MapRun(run);
    }

    public async Task<AdminManagedAiTestResponseDto> RunInteractiveTestAsync(
        AdminManagedAiTestRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _managedAi.RunAdminTestAsync(request, cancellationToken);
        var catalogModel = FindCatalogModel(request.ProviderId, request.ModelId);
        if (catalogModel != null)
        {
            _latencyRepository.UpsertModelStatus(MapStatusRecord(
                request.ProviderId,
                request.ModelId,
                catalogModel.ProviderLabel,
                catalogModel.DisplayName,
                catalogModel.SupportsVision,
                result,
                string.Empty));
        }

        return result;
    }

    public void RequeueRunningJobs()
    {
        _latencyRepository.RequeueRunningJobs();
    }

    public async Task<bool> TryProcessNextLatencyRunAsync(CancellationToken cancellationToken)
    {
        var run = _latencyRepository.TryStartNextQueued();
        if (run == null)
        {
            return false;
        }

        try
        {
            var catalogProviders = _catalogService.ListCatalogProviders();
            var models = catalogProviders
                .SelectMany(provider => provider.Models.Select(model => new CatalogModelRef
                {
                    ProviderId = provider.ProviderId,
                    ProviderLabel = provider.Label,
                    ModelId = model.ModelId,
                    DisplayName = model.DisplayName,
                    SupportsVision = model.SupportsVision
                }))
                .ToArray();

            _latencyRepository.UpdateProgress(run.JobId, models.Length, 0);

            for (var index = 0; index < models.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var model = models[index];
                var result = await _managedAi.RunAdminTestAsync(new AdminManagedAiTestRequestDto
                {
                    ProviderId = model.ProviderId,
                    ModelId = model.ModelId,
                    Messages = ProbeMessages
                }, cancellationToken);

                _latencyRepository.UpsertModelStatus(MapStatusRecord(
                    model.ProviderId,
                    model.ModelId,
                    model.ProviderLabel,
                    model.DisplayName,
                    model.SupportsVision,
                    result,
                    run.JobId));

                _latencyRepository.UpdateProgress(run.JobId, models.Length, index + 1);
            }

            _latencyRepository.MarkCompleted(run.JobId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _latencyRepository.MarkFailed(run.JobId, "Latency check interrupted during shutdown.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Managed AI latency check job {JobId} failed.", run.JobId);
            _latencyRepository.MarkFailed(run.JobId, GetSingleLineMessage(ex));
        }

        return true;
    }

    private CatalogModelRef? FindCatalogModel(string providerId, string modelId)
    {
        return _catalogService.ListCatalogProviders()
            .Where(provider => string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(provider => provider.Models.Select(model => new CatalogModelRef
            {
                ProviderId = provider.ProviderId,
                ProviderLabel = provider.Label,
                ModelId = model.ModelId,
                DisplayName = model.DisplayName,
                SupportsVision = model.SupportsVision
            }))
            .FirstOrDefault(model => string.Equals(model.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildKey(string providerId, string modelId)
    {
        return $"{providerId}::{modelId}";
    }

    private static ManagedAiLatencyRunDto MapRun(ManagedAiLatencyRunRecord record)
    {
        return new ManagedAiLatencyRunDto
        {
            JobId = record.JobId,
            Status = record.Status,
            TotalModels = record.TotalModels,
            ProcessedModels = record.ProcessedModels,
            Error = record.Error,
            RequestedAtUtc = record.RequestedAtUtc,
            StartedAtUtc = record.StartedAtUtc,
            CompletedAtUtc = record.CompletedAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc
        };
    }

    private static ManagedAiLatencyModelStatusDto MapModelStatus(
        ManagedAiProviderOptionDto provider,
        ManagedAiModelOptionDto model,
        ManagedAiLatencyModelStatusRecord? status)
    {
        return new ManagedAiLatencyModelStatusDto
        {
            ProviderId = provider.ProviderId,
            ProviderLabel = provider.Label,
            ModelId = model.ModelId,
            DisplayName = model.DisplayName,
            SupportsVision = model.SupportsVision,
            IsChatCapable = status?.IsChatCapable,
            Status = status?.Status ?? "untested",
            Message = status?.Message ?? "No pipeline check run yet.",
            LatencyMs = status?.LatencyMs,
            CheckedAtUtc = status?.CheckedAtUtc,
            LastJobId = status?.LastJobId ?? string.Empty
        };
    }

    private static ManagedAiLatencyModelStatusRecord MapStatusRecord(
        string providerId,
        string modelId,
        string providerLabel,
        string displayName,
        bool supportsVision,
        AdminManagedAiTestResponseDto result,
        string jobId)
    {
        return new ManagedAiLatencyModelStatusRecord
        {
            ProviderId = providerId,
            ModelId = modelId,
            ModelDisplayName = displayName,
            SupportsVision = supportsVision,
            IsChatCapable = result.IsChatCapable,
            Status = result.Status,
            Message = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? $"Pipeline check succeeded via {providerLabel}."
                : result.ErrorMessage,
            LatencyMs = result.LatencyMs,
            CheckedAtUtc = result.TestedAtUtc,
            LastJobId = jobId,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static string GetSingleLineMessage(Exception ex)
    {
        var message = ex.Message?.Trim() ?? "Latency check failed.";
        var newlineIndex = message.IndexOfAny(['\r', '\n']);
        return newlineIndex >= 0 ? message[..newlineIndex].Trim() : message;
    }

    private sealed class CatalogModelRef
    {
        public string ProviderId { get; set; } = string.Empty;
        public string ProviderLabel { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool SupportsVision { get; set; }
    }
}
