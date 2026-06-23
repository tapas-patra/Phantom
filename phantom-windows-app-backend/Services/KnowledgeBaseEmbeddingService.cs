using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class KnowledgeBaseEmbeddingService : IKnowledgeBaseEmbeddingService
{
    private static readonly string[] SupportedProviders = ["openai", "openai-compatible"];
    private const int MaxTransientRetries = 3;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    private readonly BackendOptions _options;
    private readonly HostedKnowledgeBaseEmbeddingConfigRepository _repository;
    private readonly SecretProtector _protector;
    private readonly ILogger<KnowledgeBaseEmbeddingService> _logger;

    public KnowledgeBaseEmbeddingService(
        BackendOptions options,
        HostedKnowledgeBaseEmbeddingConfigRepository repository,
        SecretProtector protector,
        ILogger<KnowledgeBaseEmbeddingService> logger)
    {
        _options = options;
        _repository = repository;
        _protector = protector;
        _logger = logger;
    }

    public bool IsConfigured => ResolveConfiguration().IsConfigured;

    public KnowledgeBaseEmbeddingProfile ActiveProfile
    {
        get
        {
            var configuration = ResolveConfiguration();
            return new KnowledgeBaseEmbeddingProfile
            {
                ProviderId = configuration.ProviderId,
                BaseUrl = configuration.BaseUrl,
                ModelId = configuration.ModelId,
                Dimensions = configuration.Dimensions,
                Version = configuration.Version
            };
        }
    }

    public HostedKnowledgeBaseEmbeddingConfigDto GetAdminConfiguration()
    {
        return ResolveConfiguration();
    }

    public HostedKnowledgeBaseEmbeddingConfigDto UpdateAdminConfiguration(HostedKnowledgeBaseEmbeddingConfigUpdateRequestDto request)
    {
        ValidateRequest(request);

        var existing = _repository.Get();
        var nextApiKey = string.IsNullOrWhiteSpace(request.ApiKey)
            ? existing?.EncryptedApiKey ?? string.Empty
            : _protector.Protect(request.ApiKey.Trim());
        if (request.IsEnabled && string.IsNullOrWhiteSpace(nextApiKey))
        {
            throw new BackendValidationException("An embedding API key is required when embedding is enabled.");
        }

        var record = new HostedKnowledgeBaseEmbeddingConfigRecord
        {
            ConfigId = HostedKnowledgeBaseEmbeddingConfigRepository.GlobalConfigId,
            IsEnabled = request.IsEnabled,
            ProviderId = request.ProviderId.Trim(),
            BaseUrl = request.BaseUrl.Trim().TrimEnd('/'),
            ModelId = request.ModelId.Trim(),
            Dimensions = request.Dimensions,
            Version = request.Version,
            BatchSize = request.BatchSize,
            EncryptedApiKey = nextApiKey,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _repository.Save(record);
        return Map(record, hasApiKey: !string.IsNullOrWhiteSpace(record.EncryptedApiKey), "admin-dashboard");
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        var configuration = ResolveConfiguration(includeApiKey: true);
        if (!configuration.IsConfigured)
        {
            throw new BackendValidationException("Knowledge-base embeddings are not configured.");
        }

        if (inputs.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var profile = new KnowledgeBaseEmbeddingProfile
        {
            ProviderId = configuration.ProviderId,
            BaseUrl = configuration.BaseUrl,
            ModelId = configuration.ModelId,
            Dimensions = configuration.Dimensions,
            Version = configuration.Version
        };

        var results = new List<float[]>(inputs.Count);
        foreach (var batch in Batch(inputs, Math.Max(1, configuration.BatchSize)))
        {
            results.AddRange(await GenerateBatchAsync(profile, configuration.ApiKey, batch, cancellationToken));
        }

        return results;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string input, CancellationToken cancellationToken)
    {
        var embeddings = await GenerateEmbeddingsAsync(new[] { input ?? string.Empty }, cancellationToken);
        return embeddings.Count == 0 ? Array.Empty<float>() : embeddings[0];
    }

    private HostedKnowledgeBaseEmbeddingResolvedConfig ResolveConfiguration(bool includeApiKey = false)
    {
        var stored = _repository.Get();
        if (stored != null)
        {
            var hasApiKey = !string.IsNullOrWhiteSpace(stored.EncryptedApiKey);
            var apiKey = includeApiKey && hasApiKey ? _protector.Unprotect(stored.EncryptedApiKey) : string.Empty;
            var isConfigured = IsConfigurationUsable(stored.IsEnabled, hasApiKey, stored.ProviderId, stored.ModelId, stored.Dimensions);
            return new HostedKnowledgeBaseEmbeddingResolvedConfig
            {
                IsEnabled = stored.IsEnabled,
                ProviderId = stored.ProviderId,
                BaseUrl = stored.BaseUrl,
                ModelId = stored.ModelId,
                Dimensions = stored.Dimensions,
                Version = stored.Version,
                BatchSize = stored.BatchSize,
                HasApiKey = hasApiKey,
                IsConfigured = isConfigured,
                ApiKey = apiKey,
                ConfigSource = "admin-dashboard",
                UpdatedAtUtc = stored.UpdatedAtUtc
            };
        }

        var envHasApiKey = !string.IsNullOrWhiteSpace(_options.KnowledgeBaseEmbeddingApiKey);
        var envIsConfigured = IsConfigurationUsable(
            _options.KnowledgeBaseEmbeddingEnabled,
            envHasApiKey,
            _options.KnowledgeBaseEmbeddingProvider,
            _options.KnowledgeBaseEmbeddingModel,
            _options.KnowledgeBaseEmbeddingDimensions);
        return new HostedKnowledgeBaseEmbeddingResolvedConfig
        {
            IsEnabled = _options.KnowledgeBaseEmbeddingEnabled,
            ProviderId = _options.KnowledgeBaseEmbeddingProvider,
            BaseUrl = _options.KnowledgeBaseEmbeddingBaseUrl.TrimEnd('/'),
            ModelId = _options.KnowledgeBaseEmbeddingModel,
            Dimensions = _options.KnowledgeBaseEmbeddingDimensions,
            Version = _options.KnowledgeBaseEmbeddingVersion,
            BatchSize = _options.KnowledgeBaseEmbeddingBatchSize,
            HasApiKey = envHasApiKey,
            IsConfigured = envIsConfigured,
            ApiKey = includeApiKey ? _options.KnowledgeBaseEmbeddingApiKey : string.Empty,
            ConfigSource = "env-default",
            UpdatedAtUtc = null
        };
    }

    private async Task<IReadOnlyList<float[]>> GenerateBatchAsync(
        KnowledgeBaseEmbeddingProfile profile,
        string apiKey,
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                using var request = BuildEmbeddingRequest(profile, apiKey, inputs);
                using var response = await HttpClient.SendAsync(request, cancellationToken);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw CreateProviderException(response.StatusCode, response.Headers.RetryAfter?.Delta, responseText);
                }

                return ParseEmbeddingResponse(profile, responseText);
            }
            catch (EmbeddingProviderException ex) when (ex.IsTransient && attempt <= MaxTransientRetries)
            {
                var delay = GetRetryDelay(attempt, ex.RetryAfterSeconds);
                _logger.LogWarning(
                    "Transient KB embedding error from provider. Attempt {Attempt}/{MaxAttempts}. Retrying in {DelaySeconds}s. Status {StatusCode}.",
                    attempt,
                    MaxTransientRetries + 1,
                    delay.TotalSeconds,
                    ex.ProviderStatusCode);
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt <= MaxTransientRetries)
            {
                var delay = GetRetryDelay(attempt, null);
                _logger.LogWarning(
                    ex,
                    "Network failure during KB embedding request. Attempt {Attempt}/{MaxAttempts}. Retrying in {DelaySeconds}s.",
                    attempt,
                    MaxTransientRetries + 1,
                    delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt <= MaxTransientRetries)
            {
                var delay = GetRetryDelay(attempt, null);
                _logger.LogWarning(
                    ex,
                    "Timed out during KB embedding request. Attempt {Attempt}/{MaxAttempts}. Retrying in {DelaySeconds}s.",
                    attempt,
                    MaxTransientRetries + 1,
                    delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new EmbeddingProviderException(
                    "Knowledge-base embedding request failed because the embedding provider could not be reached.",
                    isTransient: true,
                    innerException: ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new EmbeddingProviderException(
                    "Knowledge-base embedding request timed out.",
                    isTransient: true,
                    innerException: ex);
            }
        }
    }

    private static HostedKnowledgeBaseEmbeddingConfigDto Map(
        HostedKnowledgeBaseEmbeddingConfigRecord record,
        bool hasApiKey,
        string source)
    {
        return new HostedKnowledgeBaseEmbeddingConfigDto
        {
            IsEnabled = record.IsEnabled,
            ProviderId = record.ProviderId,
            BaseUrl = record.BaseUrl,
            ModelId = record.ModelId,
            Dimensions = record.Dimensions,
            Version = record.Version,
            BatchSize = record.BatchSize,
            HasApiKey = hasApiKey,
            IsConfigured = IsConfigurationUsable(record.IsEnabled, hasApiKey, record.ProviderId, record.ModelId, record.Dimensions),
            ConfigSource = source,
            UpdatedAtUtc = record.UpdatedAtUtc
        };
    }

    private static bool IsConfigurationUsable(bool isEnabled, bool hasApiKey, string providerId, string modelId, int dimensions)
    {
        return isEnabled
            && hasApiKey
            && !string.IsNullOrWhiteSpace(modelId)
            && dimensions == HostedKnowledgeBaseEmbeddingDefaults.DefaultDimensions
            && SupportedProviders.Contains(providerId, StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidateRequest(HostedKnowledgeBaseEmbeddingConfigUpdateRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderId))
        {
            throw new BackendValidationException("Embedding provider is required.");
        }

        if (!SupportedProviders.Contains(request.ProviderId.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            throw new BackendValidationException("Embedding provider must be openai or openai-compatible.");
        }

        if (string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            throw new BackendValidationException("Embedding base URL is required.");
        }

        if (!Uri.TryCreate(request.BaseUrl.Trim(), UriKind.Absolute, out _))
        {
            throw new BackendValidationException("Embedding base URL must be a valid absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            throw new BackendValidationException("Embedding model is required.");
        }

        if (request.Dimensions != HostedKnowledgeBaseEmbeddingDefaults.DefaultDimensions)
        {
            throw new BackendValidationException(
                $"Embedding dimensions must stay at {HostedKnowledgeBaseEmbeddingDefaults.DefaultDimensions} for the current indexed storage layout.");
        }

        if (request.Version <= 0)
        {
            throw new BackendValidationException("Embedding version must be greater than 0.");
        }

        if (request.BatchSize <= 0 || request.BatchSize > 256)
        {
            throw new BackendValidationException("Embedding batch size must be between 1 and 256.");
        }
    }

    private static IEnumerable<IReadOnlyList<string>> Batch(IReadOnlyList<string> inputs, int batchSize)
    {
        for (var index = 0; index < inputs.Count; index += batchSize)
        {
            var count = Math.Min(batchSize, inputs.Count - index);
            var batch = new string[count];
            for (var offset = 0; offset < count; offset++)
            {
                batch[offset] = inputs[index + offset];
            }

            yield return batch;
        }
    }

    private sealed class HostedKnowledgeBaseEmbeddingResolvedConfig : HostedKnowledgeBaseEmbeddingConfigDto
    {
        public string ApiKey { get; init; } = string.Empty;
    }

    private static HttpRequestMessage BuildEmbeddingRequest(
        KnowledgeBaseEmbeddingProfile profile,
        string apiKey,
        IReadOnlyList<string> inputs)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{profile.BaseUrl.TrimEnd('/')}/embeddings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = profile.ModelId,
            ["input"] = inputs
        };

        if (profile.ProviderId.Equals("openai", StringComparison.OrdinalIgnoreCase)
            && profile.Dimensions > 0)
        {
            payload["dimensions"] = profile.Dimensions;
        }

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return request;
    }

    private static IReadOnlyList<float[]> ParseEmbeddingResponse(KnowledgeBaseEmbeddingProfile profile, string responseText)
    {
        using var document = JsonDocument.Parse(responseText);
        if (!document.RootElement.TryGetProperty("data", out var dataElement)
            || dataElement.ValueKind != JsonValueKind.Array)
        {
            throw new EmbeddingProviderException("Knowledge-base embedding response did not include data.", isTransient: false);
        }

        var results = new List<float[]>(dataElement.GetArrayLength());
        foreach (var item in dataElement.EnumerateArray())
        {
            if (!item.TryGetProperty("embedding", out var embeddingElement)
                || embeddingElement.ValueKind != JsonValueKind.Array)
            {
                throw new EmbeddingProviderException(
                    "Knowledge-base embedding response did not include an embedding array.",
                    isTransient: false);
            }

            var vector = embeddingElement.EnumerateArray().Select(value => value.GetSingle()).ToArray();
            if (vector.Length != profile.Dimensions)
            {
                throw new EmbeddingProviderException(
                    $"Knowledge-base embedding dimension mismatch. Expected {profile.Dimensions}, got {vector.Length}.",
                    isTransient: false);
            }

            results.Add(vector);
        }

        return results;
    }

    private static EmbeddingProviderException CreateProviderException(
        System.Net.HttpStatusCode statusCode,
        TimeSpan? retryAfter,
        string responseText)
    {
        var numericStatusCode = (int)statusCode;
        var isTransient = numericStatusCode == 408
            || numericStatusCode == 429
            || numericStatusCode >= 500;
        var retryAfterSeconds = retryAfter.HasValue
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.Value.TotalSeconds))
            : null;
        var message = numericStatusCode == 429
            ? "Knowledge-base embedding provider rate-limited the request."
            : $"Knowledge-base embedding request failed with status {numericStatusCode}.";

        if (!string.IsNullOrWhiteSpace(responseText))
        {
            message = $"{message} {responseText}";
        }

        return new EmbeddingProviderException(
            message,
            isTransient,
            numericStatusCode,
            retryAfterSeconds);
    }

    private static TimeSpan GetRetryDelay(int attempt, int? retryAfterSeconds)
    {
        if (retryAfterSeconds.HasValue && retryAfterSeconds.Value > 0)
        {
            return TimeSpan.FromSeconds(Math.Min(retryAfterSeconds.Value, 30));
        }

        var seconds = Math.Min(Math.Pow(2, Math.Max(0, attempt - 1)), 8);
        return TimeSpan.FromSeconds(seconds);
    }
}
