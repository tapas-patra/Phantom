using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class KnowledgeBaseEmbeddingService : IKnowledgeBaseEmbeddingService
{
    private const int MaxIndexingTransientRetries = 3;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    private readonly BackendOptions _options;
    private readonly HostedKnowledgeBaseEmbeddingConfigRepository _repository;
    private readonly SecretProtector _protector;
    private readonly ILogger<KnowledgeBaseEmbeddingService> _logger;
    private readonly ConcurrentDictionary<string, CachedQueryEmbeddingEntry> _queryEmbeddingCache = new(StringComparer.Ordinal);
    private readonly TimeSpan _queryTimeout;
    private readonly int _queryMaxRetries;
    private readonly TimeSpan _queryRetryDelay;
    private readonly int _queryCacheMaxEntries;
    private readonly TimeSpan _queryCacheTtl;

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
        _queryTimeout = TimeSpan.FromMilliseconds(Math.Clamp(options.KnowledgeBaseQueryEmbeddingTimeoutMs, 100, 2000));
        _queryMaxRetries = Math.Clamp(options.KnowledgeBaseQueryEmbeddingRetries, 0, 3);
        _queryRetryDelay = TimeSpan.FromMilliseconds(Math.Clamp(options.KnowledgeBaseQueryEmbeddingRetryDelayMs, 0, 1000));
        _queryCacheMaxEntries = Math.Clamp(options.KnowledgeBaseQueryEmbeddingCacheEntries, 32, 4096);
        _queryCacheTtl = TimeSpan.FromMinutes(Math.Clamp(options.KnowledgeBaseQueryEmbeddingCacheTtlMinutes, 1, 240));
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
        return ToDto(ResolveConfiguration());
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
        _queryEmbeddingCache.Clear();
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

        var profile = ToProfile(configuration);
        var results = new List<float[]>(inputs.Count);
        foreach (var batch in Batch(inputs, Math.Max(1, configuration.BatchSize)))
        {
            results.AddRange(await GenerateBatchAsync(
                profile,
                configuration.ApiKey,
                batch,
                cancellationToken,
                new EmbeddingRequestPolicy(
                    operationName: "batch",
                    maxRetries: MaxIndexingTransientRetries,
                    timeout: null,
                    retryDelay: TimeSpan.FromSeconds(1))));
        }

        return results;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string input, CancellationToken cancellationToken)
    {
        var embeddings = await GenerateEmbeddingsAsync(new[] { input ?? string.Empty }, cancellationToken);
        return embeddings.Count == 0 ? Array.Empty<float>() : embeddings[0];
    }

    public async Task<float[]> GenerateQueryEmbeddingAsync(string input, CancellationToken cancellationToken)
    {
        var configuration = ResolveConfiguration(includeApiKey: true);
        if (!configuration.IsConfigured)
        {
            throw new BackendValidationException("Knowledge-base embeddings are not configured.");
        }

        var normalizedInput = NormalizeInput(input);
        if (normalizedInput.Length == 0)
        {
            return Array.Empty<float>();
        }

        var cacheKey = BuildQueryCacheKey(configuration, normalizedInput);
        if (TryGetCachedQueryEmbedding(cacheKey, out var cachedEmbedding))
        {
            _logger.LogDebug("KB query embedding cache hit for profile {Profile}.", cacheKey[..Math.Min(cacheKey.Length, 80)]);
            return cachedEmbedding;
        }

        var profile = ToProfile(configuration);
        var vectors = await GenerateBatchAsync(
            profile,
            configuration.ApiKey,
            new[] { normalizedInput },
            cancellationToken,
            new EmbeddingRequestPolicy(
                operationName: "query",
                maxRetries: _queryMaxRetries,
                timeout: _queryTimeout,
                retryDelay: _queryRetryDelay));
        var result = vectors.Count == 0 ? Array.Empty<float>() : vectors[0];
        if (result.Length == profile.Dimensions)
        {
            _queryEmbeddingCache[cacheKey] = new CachedQueryEmbeddingEntry
            {
                CachedAtUtc = DateTime.UtcNow,
                Values = result.ToArray()
            };
            TrimQueryEmbeddingCacheIfNeeded();
        }

        return result;
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
        CancellationToken cancellationToken,
        EmbeddingRequestPolicy policy)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (policy.Timeout.HasValue)
            {
                attemptCts.CancelAfter(policy.Timeout.Value);
            }

            try
            {
                using var response = await SendEmbeddingRequestAsync(profile, apiKey, inputs, attemptCts.Token);
                var responseText = await response.Content.ReadAsStringAsync(attemptCts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    throw CreateProviderException(response.StatusCode, response.Headers.RetryAfter?.Delta, responseText);
                }

                return ParseEmbeddingResponse(profile, responseText);
            }
            catch (EmbeddingProviderException ex) when (ex.IsTransient && attempt <= policy.MaxRetries)
            {
                var delay = GetRetryDelay(attempt, ex.RetryAfterSeconds, policy.RetryDelay, maxDelaySeconds: 8);
                _logger.LogWarning(
                    "Transient KB {Operation} embedding error. Attempt {Attempt}/{MaxAttempts}. Retrying in {DelayMs}ms. Status {StatusCode}.",
                    policy.OperationName,
                    attempt,
                    policy.MaxRetries + 1,
                    delay.TotalMilliseconds,
                    ex.ProviderStatusCode);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (HttpRequestException ex) when (attempt <= policy.MaxRetries)
            {
                var delay = GetRetryDelay(attempt, null, policy.RetryDelay, maxDelaySeconds: 8);
                _logger.LogWarning(
                    ex,
                    "Network failure during KB {Operation} embedding request. Attempt {Attempt}/{MaxAttempts}. Retrying in {DelayMs}ms.",
                    policy.OperationName,
                    attempt,
                    policy.MaxRetries + 1,
                    delay.TotalMilliseconds);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (OperationCanceledException ex)
                when (!cancellationToken.IsCancellationRequested
                    && attemptCts.IsCancellationRequested
                    && attempt <= policy.MaxRetries)
            {
                var delay = GetRetryDelay(attempt, null, policy.RetryDelay, maxDelaySeconds: 8);
                _logger.LogWarning(
                    ex,
                    "Timed out during KB {Operation} embedding request. Attempt {Attempt}/{MaxAttempts}. Retrying in {DelayMs}ms.",
                    policy.OperationName,
                    attempt,
                    policy.MaxRetries + 1,
                    delay.TotalMilliseconds);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (HttpRequestException ex)
            {
                throw new EmbeddingProviderException(
                    "Knowledge-base embedding request failed because the embedding provider could not be reached.",
                    isTransient: true,
                    innerException: ex);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && attemptCts.IsCancellationRequested)
            {
                throw new EmbeddingProviderException(
                    "Knowledge-base embedding request timed out.",
                    isTransient: true,
                    innerException: ex);
            }
        }
    }

    private bool TryGetCachedQueryEmbedding(string cacheKey, out float[] embedding)
    {
        if (_queryEmbeddingCache.TryGetValue(cacheKey, out var cached)
            && DateTime.UtcNow - cached.CachedAtUtc <= _queryCacheTtl)
        {
            embedding = cached.Values.ToArray();
            return true;
        }

        _queryEmbeddingCache.TryRemove(cacheKey, out _);
        embedding = Array.Empty<float>();
        return false;
    }

    private void TrimQueryEmbeddingCacheIfNeeded()
    {
        if (_queryEmbeddingCache.Count <= _queryCacheMaxEntries)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var entry in _queryEmbeddingCache)
        {
            if (now - entry.Value.CachedAtUtc > _queryCacheTtl)
            {
                _queryEmbeddingCache.TryRemove(entry.Key, out _);
            }
        }

        if (_queryEmbeddingCache.Count <= _queryCacheMaxEntries)
        {
            return;
        }

        foreach (var entry in _queryEmbeddingCache.OrderBy(item => item.Value.CachedAtUtc).Take(_queryEmbeddingCache.Count - _queryCacheMaxEntries))
        {
            _queryEmbeddingCache.TryRemove(entry.Key, out _);
        }
    }

    private static KnowledgeBaseEmbeddingProfile ToProfile(HostedKnowledgeBaseEmbeddingResolvedConfig configuration)
    {
        return new KnowledgeBaseEmbeddingProfile
        {
            ProviderId = configuration.ProviderId,
            BaseUrl = configuration.BaseUrl,
            ModelId = configuration.ModelId,
            Dimensions = configuration.Dimensions,
            Version = configuration.Version
        };
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
            && !string.IsNullOrWhiteSpace(providerId)
            && !string.IsNullOrWhiteSpace(modelId)
            && dimensions > 0
            && dimensions <= HostedKnowledgeBaseEmbeddingDefaults.MaxDimensions;
    }

    private static void ValidateRequest(HostedKnowledgeBaseEmbeddingConfigUpdateRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderId))
        {
            throw new BackendValidationException("Embedding provider is required.");
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

        if (request.Dimensions <= 0 || request.Dimensions > HostedKnowledgeBaseEmbeddingDefaults.MaxDimensions)
        {
            throw new BackendValidationException(
                $"Embedding dimensions must be between 1 and {HostedKnowledgeBaseEmbeddingDefaults.MaxDimensions}.");
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

    private static HostedKnowledgeBaseEmbeddingConfigDto ToDto(HostedKnowledgeBaseEmbeddingResolvedConfig configuration)
    {
        return new HostedKnowledgeBaseEmbeddingConfigDto
        {
            IsEnabled = configuration.IsEnabled,
            ProviderId = configuration.ProviderId,
            BaseUrl = configuration.BaseUrl,
            ModelId = configuration.ModelId,
            Dimensions = configuration.Dimensions,
            Version = configuration.Version,
            BatchSize = configuration.BatchSize,
            HasApiKey = configuration.HasApiKey,
            IsConfigured = configuration.IsConfigured,
            ConfigSource = configuration.ConfigSource,
            UpdatedAtUtc = configuration.UpdatedAtUtc
        };
    }

    private sealed class HostedKnowledgeBaseEmbeddingResolvedConfig
    {
        public bool IsEnabled { get; init; }
        public string ProviderId { get; init; } = string.Empty;
        public string BaseUrl { get; init; } = string.Empty;
        public string ModelId { get; init; } = string.Empty;
        public int Dimensions { get; init; }
        public int Version { get; init; }
        public int BatchSize { get; init; }
        public bool HasApiKey { get; init; }
        public bool IsConfigured { get; init; }
        public string ConfigSource { get; init; } = string.Empty;
        public DateTime? UpdatedAtUtc { get; init; }
        public string ApiKey { get; init; } = string.Empty;
    }

    private sealed class EmbeddingRequestPolicy
    {
        public EmbeddingRequestPolicy(string operationName, int maxRetries, TimeSpan? timeout, TimeSpan retryDelay)
        {
            OperationName = operationName;
            MaxRetries = Math.Max(0, maxRetries);
            Timeout = timeout;
            RetryDelay = retryDelay < TimeSpan.Zero ? TimeSpan.Zero : retryDelay;
        }

        public string OperationName { get; }
        public int MaxRetries { get; }
        public TimeSpan? Timeout { get; }
        public TimeSpan RetryDelay { get; }
    }

    private sealed class CachedQueryEmbeddingEntry
    {
        public DateTime CachedAtUtc { get; init; }
        public float[] Values { get; init; } = Array.Empty<float>();
    }

    private static HttpRequestMessage BuildEmbeddingRequest(
        KnowledgeBaseEmbeddingProfile profile,
        string apiKey,
        IReadOnlyList<string> inputs,
        string? requestUrl = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl ?? $"{profile.BaseUrl.TrimEnd('/')}/embeddings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = profile.ModelId,
            ["input"] = inputs
        };

        if (SupportsDimensionsOverride(profile.ProviderId) && profile.Dimensions > 0)
        {
            payload["dimensions"] = profile.Dimensions;
        }

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return request;
    }

    private static async Task<HttpResponseMessage> SendEmbeddingRequestAsync(
        KnowledgeBaseEmbeddingProfile profile,
        string apiKey,
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken)
    {
        var primaryUrl = $"{profile.BaseUrl.TrimEnd('/')}/embeddings";
        using (var primaryRequest = BuildEmbeddingRequest(profile, apiKey, inputs, primaryUrl))
        {
            var primaryResponse = await HttpClient.SendAsync(primaryRequest, cancellationToken);
            if (primaryResponse.StatusCode != System.Net.HttpStatusCode.NotFound || HasVersionSegment(profile.BaseUrl))
            {
                return primaryResponse;
            }

            primaryResponse.Dispose();
        }

        var fallbackUrl = $"{profile.BaseUrl.TrimEnd('/')}/v1/embeddings";
        return await HttpClient.SendAsync(BuildEmbeddingRequest(profile, apiKey, inputs, fallbackUrl), cancellationToken);
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
        int? retryAfterSeconds = retryAfter.HasValue
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

    private static TimeSpan GetRetryDelay(int attempt, int? retryAfterSeconds, TimeSpan fallbackDelay, int maxDelaySeconds)
    {
        if (retryAfterSeconds.HasValue && retryAfterSeconds.Value > 0)
        {
            return TimeSpan.FromSeconds(Math.Min(retryAfterSeconds.Value, maxDelaySeconds));
        }

        if (fallbackDelay > TimeSpan.Zero)
        {
            return fallbackDelay;
        }

        var seconds = Math.Min(Math.Pow(2, Math.Max(0, attempt - 1)), maxDelaySeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private static string NormalizeInput(string value)
    {
        return string.Join(" ", (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static string BuildQueryCacheKey(HostedKnowledgeBaseEmbeddingResolvedConfig configuration, string normalizedInput)
    {
        var queryHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedInput)));
        return string.Join(
            ":",
            configuration.ProviderId,
            configuration.ModelId,
            configuration.Version.ToString(),
            configuration.Dimensions.ToString(),
            queryHash);
    }

    private static bool SupportsDimensionsOverride(string providerId)
    {
        return providerId.Equals("openai", StringComparison.OrdinalIgnoreCase)
            || providerId.Equals("openai-compatible", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasVersionSegment(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase);
    }
}
