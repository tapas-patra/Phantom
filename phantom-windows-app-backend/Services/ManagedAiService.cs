using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class ManagedAiService
{
    private const int AdminModelTimeoutMs = 20000;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    private readonly ManagedProviderCredentialRepository _credentials;
    private readonly SecretProtector _protector;
    private readonly AuthSessionRepository _sessions;
    private readonly AccountRepository _accounts;
    private readonly TokenService _tokens;
    private readonly ManagedAiCatalogService _catalogService;
    private readonly ILogger<ManagedAiService> _logger;

    public ManagedAiService(
        ManagedProviderCredentialRepository credentials,
        SecretProtector protector,
        AuthSessionRepository sessions,
        AccountRepository accounts,
        TokenService tokens,
        ManagedAiCatalogService catalogService,
        ILogger<ManagedAiService> logger)
    {
        _credentials = credentials;
        _protector = protector;
        _sessions = sessions;
        _accounts = accounts;
        _tokens = tokens;
        _catalogService = catalogService;
        _logger = logger;
    }

    public ManagedAiCatalogDto GetCatalogForAccount(DesktopAccountRecord account)
    {
        return _catalogService.GetCatalogForAccount(account);
    }

    public IReadOnlyList<ManagedAiProviderKeyDto> ListAdminCredentials()
    {
        return _credentials.ListAll()
            .Select(record => new ManagedAiProviderKeyDto
            {
                CredentialId = record.CredentialId,
                ProviderId = record.ProviderId,
                Label = record.Label,
                IsEnabled = record.IsEnabled,
                Priority = record.Priority,
                CooldownUntilUtc = record.CooldownUntilUtc,
                LastFailureCode = record.LastFailureCode,
                UpdatedAtUtc = record.UpdatedAtUtc
            })
            .ToArray();
    }

    public ManagedAiProviderKeyDto UpsertCredential(ManagedAiProviderKeyUpsertRequestDto request)
    {
        if (!ManagedAiCatalog.IsAllowedProvider(request.ProviderId))
        {
            throw new BackendValidationException("Unsupported managed provider.");
        }

        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            throw new BackendValidationException("API key is required.");
        }

        var now = DateTime.UtcNow;
        var existing = !string.IsNullOrWhiteSpace(request.CredentialId)
            ? _credentials.FindById(request.CredentialId)
            : null;
        if (existing != null && !string.Equals(existing.Workload, "chat", StringComparison.Ordinal))
        {
            throw new BackendValidationException("Credential belongs to a different workload.");
        }

        var record = existing ?? new ManagedProviderCredentialRecord
        {
            CredentialId = string.IsNullOrWhiteSpace(request.CredentialId)
                ? $"managed-key-{Guid.NewGuid():N}"
                : request.CredentialId,
            CreatedAtUtc = now
        };
        record.Workload = "chat";

        record.ProviderId = request.ProviderId.Trim();
        record.Label = string.IsNullOrWhiteSpace(request.Label) ? request.ProviderId.Trim() : request.Label.Trim();
        record.EncryptedApiKey = _protector.Protect(request.ApiKey.Trim());
        record.IsEnabled = request.IsEnabled;
        record.Priority = request.Priority;
        record.CooldownUntilUtc = null;
        record.LastFailureCode = string.Empty;
        record.ConsecutiveFailureCount = 0;
        record.UpdatedAtUtc = now;

        _credentials.Save(record);
        return new ManagedAiProviderKeyDto
        {
            CredentialId = record.CredentialId,
            ProviderId = record.ProviderId,
            Label = record.Label,
            IsEnabled = record.IsEnabled,
            Priority = record.Priority,
            CooldownUntilUtc = record.CooldownUntilUtc,
            LastFailureCode = record.LastFailureCode,
            UpdatedAtUtc = record.UpdatedAtUtc
        };
    }

    public void DeleteCredential(string credentialId)
    {
        if (string.IsNullOrWhiteSpace(credentialId))
        {
            throw new BackendValidationException("credentialId is required.");
        }

        var record = _credentials.FindById(credentialId.Trim())
            ?? throw new BackendValidationException("Managed AI credential not found.");
        if (!string.Equals(record.Workload, "chat", StringComparison.Ordinal))
            throw new BackendValidationException("Credential belongs to a different workload.");
        _credentials.Delete(record.CredentialId);
    }

    public DesktopAccountRecord RequireManagedAccountFromAccessToken(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new BackendValidationException("Authorization bearer token is required.");
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new BackendValidationException("Authorization bearer token is required.");
        }

        var session = _sessions.FindByAccessTokenHash(_tokens.HashToken(token))
            ?? throw new BackendValidationException("Desktop session not found.");

        if (!session.IsAuthenticated || session.RevokedAtUtc.HasValue || session.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new BackendValidationException("Desktop session is no longer valid.");
        }

        var account = _accounts.FindByUserId(session.UserId)
            ?? throw new BackendValidationException("Account not found.");

        return account;
    }

    public async Task StreamChatAsync(HttpResponse response, DesktopAccountRecord account, DesktopAiChatRequestDto request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        LogTiming(request, "managed_request_started", stopwatch);
        EnsureManagedAccess(account, request.AllowPaidSessionExtension);

        if (string.IsNullOrWhiteSpace(request.Provider) || !ManagedAiCatalog.IsAllowedProvider(request.Provider))
        {
            throw new BackendValidationException("Unsupported managed provider.");
        }

        if (string.IsNullOrWhiteSpace(request.Model) || !_catalogService.IsAllowedModel(request.Provider, request.Model))
        {
            throw new BackendValidationException("Unsupported managed model.");
        }

        var attachedImages = request.GetNormalizedImages();
        if (attachedImages.Count > 0
            && !_catalogService.ModelSupportsVision(request.Provider, request.Model))
        {
            throw new BackendValidationException("The selected managed model is not marked as vision-capable.");
        }

        if (request.Messages == null || request.Messages.Count == 0)
        {
            throw new BackendValidationException("At least one chat message is required.");
        }

        var providerCredentials = GetEnabledProviderCredentials(request.Provider);

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache, no-transform";
        response.Headers["X-Accel-Buffering"] = "no";
        CancellationTokenSource? firstTokenDeadline = null;

        Exception? lastError = null;
        for (var credentialIndex = 0; credentialIndex < providerCredentials.Length; credentialIndex++)
        {
            var credential = providerCredentials[credentialIndex];
            var attempt = credentialIndex + 1;
            var streamWriter = new SseDeltaWriter(
                response,
                milestone => LogTiming(request, milestone, stopwatch),
                () => firstTokenDeadline?.CancelAfter(Timeout.InfiniteTimeSpan));
            try
            {
                LogProviderAttempt(request.Provider, request.Model, request.RequestId, request.RequestId, request.TurnId, attempt, credentialIndex + 1);
                var apiKey = _protector.Unprotect(credential.EncryptedApiKey);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                firstTokenDeadline = deadline;
                deadline.CancelAfter(ReasoningPlanner.For(request.Messages, request.QuestionType).FirstTokenDeadline);
                await StreamProviderAsync(streamWriter, request, apiKey, deadline.Token);
                await streamWriter.FlushAsync(cancellationToken);
                RecordCredentialSuccess(credential);
                _logger.LogInformation(
                    "managed_ai_stream_completed service={Service} component={Component} event={Event} request_id={RequestId} operation_id={OperationId} turn_id={TurnId} provider={Provider} model={Model} execution_lane={ExecutionLane} elapsed_ms={ElapsedMs} finish_reason={FinishReason} chunk_count={ChunkCount} buffered_characters={BufferedCharacters} flush_count={FlushCount} outcome={Outcome}",
                    "phantom-windows-app-backend", "managed_ai", "provider_stream_completed", request.RequestId, request.RequestId, request.TurnId,
                    request.Provider, request.Model, "managed", stopwatch.Elapsed.TotalMilliseconds, streamWriter.FinishReason,
                    streamWriter.ChunkCount, streamWriter.BufferedCharacters, streamWriter.FlushCount, "success");
                await WriteSseDataAsync(response, "[DONE]", cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !streamWriter.HasWritten)
            {
                lastError = new TimeoutException("provider_first_token_timeout");
                var failure = ProviderResiliencePolicy.Classify(lastError);
                RecordCredentialFailure(credential, failure);
                _logger.LogWarning(
                    "managed_ai_first_token_timeout service={Service} component={Component} event={Event} request_id={RequestId} operation_id={OperationId} turn_id={TurnId} provider={Provider} model={Model} execution_lane={ExecutionLane} attempt={Attempt} credential_slot={CredentialSlot} elapsed_ms={ElapsedMs} error_code={ErrorCode} failure_class={FailureClass} outcome={Outcome}",
                    "phantom-windows-app-backend", "managed_ai", "provider_first_token_timeout", request.RequestId, request.RequestId, request.TurnId,
                    request.Provider, request.Model, "managed", attempt, credentialIndex + 1, stopwatch.Elapsed.TotalMilliseconds,
                    failure.ErrorCode, failure.Kind.ToString().ToLowerInvariant(), "retry");
                if (!ProviderResiliencePolicy.CanRetry(failure, attempt, streamWriter.HasWritten))
                    throw ManagedAiProviderException.FromFailure(lastError);
                LogProviderRetry(request.Provider, request.Model, request.RequestId, request.RequestId, request.TurnId, attempt + 1, failure);
            }
            catch (Exception ex) when (ex is not BackendValidationException && ex is not OperationCanceledException)
            {
                lastError = ex;
                var failure = ProviderResiliencePolicy.Classify(ex);
                RecordCredentialFailure(credential, failure);
                _logger.LogWarning(
                    "managed_ai_stream_failed service={Service} component={Component} event={Event} request_id={RequestId} operation_id={OperationId} turn_id={TurnId} provider={Provider} model={Model} execution_lane={ExecutionLane} attempt={Attempt} credential_slot={CredentialSlot} elapsed_ms={ElapsedMs} error_code={ErrorCode} failure_class={FailureClass} chunk_count={ChunkCount} buffered_characters={BufferedCharacters} error_detail={ErrorDetail} outcome={Outcome}",
                    "phantom-windows-app-backend", "managed_ai", "provider_stream_failed", request.RequestId, request.RequestId, request.TurnId,
                    request.Provider, request.Model, "managed", attempt, credentialIndex + 1, stopwatch.Elapsed.TotalMilliseconds,
                    failure.ErrorCode, failure.Kind.ToString().ToLowerInvariant(), streamWriter.ChunkCount,
                    streamWriter.BufferedCharacters, TruncateForLog(ex.Message), "error");
                if (streamWriter.HasWritten || response.HasStarted)
                {
                    await streamWriter.FlushAsync(cancellationToken);
                    await WriteSseJsonAsync(response, new { error = "The managed provider stream ended unexpectedly." }, cancellationToken);
                    return;
                }
                if (!ProviderResiliencePolicy.CanRetry(failure, attempt, streamWriter.HasWritten))
                    throw ManagedAiProviderException.FromFailure(ex);
                LogProviderRetry(request.Provider, request.Model, request.RequestId, request.RequestId, request.TurnId, attempt + 1, failure);
            }
        }

        throw ManagedAiProviderException.FromFailure(lastError);
    }

    public async Task<string> GenerateManagedResponseAsync(
        DesktopAccountRecord account,
        string provider,
        string model,
        bool allowPaidSessionExtension,
        IReadOnlyList<DesktopAiChatMessageDto> messages,
        CancellationToken cancellationToken,
        int maxOutputTokens = 64)
    {
        EnsureManagedAccess(account, allowPaidSessionExtension);
        if (string.IsNullOrWhiteSpace(provider) || !ManagedAiCatalog.IsAllowedProvider(provider))
        {
            throw new BackendValidationException("Unsupported managed provider.");
        }
        if (string.IsNullOrWhiteSpace(model) || !_catalogService.IsAllowedModel(provider, model))
        {
            throw new BackendValidationException("Unsupported managed model.");
        }
        if (messages == null || messages.Count == 0)
        {
            throw new BackendValidationException("At least one chat message is required.");
        }

        return await GenerateProviderResponseWithFallbackAsync(provider, model, messages, null, cancellationToken, maxOutputTokens);
    }

    private void LogTiming(DesktopAiChatRequestDto request, string milestone, Stopwatch stopwatch)
    {
        _logger.LogInformation(
            "managed_ai_milestone service={Service} component={Component} event={Event} request_id={RequestId} operation_id={OperationId} turn_id={TurnId} stage={Stage} execution_lane={ExecutionLane} elapsed_ms={ElapsedMs} provider={Provider} model={Model} image_present={ImagePresent}",
            "phantom-windows-app-backend", "managed_ai", milestone, request.RequestId, request.RequestId, request.TurnId,
            milestone, "managed", stopwatch.Elapsed.TotalMilliseconds, request.Provider, request.Model,
            !string.IsNullOrWhiteSpace(request.ImageBase64) || (request.ImagesBase64?.Count > 0));
    }

    public async Task<AdminManagedAiTestResponseDto> RunAdminTestAsync(
        AdminManagedAiTestRequestDto request,
        CancellationToken cancellationToken)
    {
        ValidateAdminTestRequest(request);

        var testedAtUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(AdminModelTimeoutMs);

            var outputText = await GenerateProviderResponseWithFallbackAsync(
                request.ProviderId,
                request.ModelId,
                request.Messages,
                string.IsNullOrWhiteSpace(request.ImageBase64)
                    ? Array.Empty<string>()
                    : new[] { request.ImageBase64 },
                timeoutCts.Token);

            stopwatch.Stop();
            return new AdminManagedAiTestResponseDto
            {
                ProviderId = request.ProviderId.Trim(),
                ModelId = request.ModelId.Trim(),
                Status = "ok",
                OutputText = NormalizeResponseText(outputText),
                ErrorMessage = string.Empty,
                LatencyMs = ToLatencyMs(stopwatch.ElapsedMilliseconds),
                IsChatCapable = true,
                TestedAtUtc = testedAtUtc
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new AdminManagedAiTestResponseDto
            {
                ProviderId = request.ProviderId.Trim(),
                ModelId = request.ModelId.Trim(),
                Status = "timeout",
                OutputText = string.Empty,
                ErrorMessage = "Model response exceeded 20 seconds.",
                LatencyMs = ToLatencyMs(stopwatch.ElapsedMilliseconds),
                IsChatCapable = null,
                TestedAtUtc = testedAtUtc
            };
        }
        catch (Exception ex) when (ex is not BackendValidationException)
        {
            stopwatch.Stop();
            return new AdminManagedAiTestResponseDto
            {
                ProviderId = request.ProviderId.Trim(),
                ModelId = request.ModelId.Trim(),
                Status = ClassifyAdminFailure(ex),
                OutputText = string.Empty,
                ErrorMessage = GetSingleLineMessage(ex),
                LatencyMs = ToLatencyMs(stopwatch.ElapsedMilliseconds),
                IsChatCapable = InferChatCapability(ex),
                TestedAtUtc = testedAtUtc
            };
        }
    }

    private static void ValidateAdminTestRequest(AdminManagedAiTestRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderId) || !ManagedAiCatalog.IsAllowedProvider(request.ProviderId))
        {
            throw new BackendValidationException("Unsupported managed provider.");
        }

        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            throw new BackendValidationException("ModelId is required.");
        }

        if (request.Messages == null || request.Messages.Count == 0)
        {
            throw new BackendValidationException("At least one chat message is required.");
        }
    }

    private static void EnsureManagedAccess(DesktopAccountRecord account, bool allowPaidSessionExtension)
    {
        var effectiveTier = AccessModeResolver.GetEffectiveAccessTier(account);
        var isFreeTier = string.Equals(effectiveTier, AccessModeResolver.Free, StringComparison.OrdinalIgnoreCase);
        var hasPremiumManagedLane = string.Equals(effectiveTier, AccessModeResolver.Premium, StringComparison.OrdinalIgnoreCase);
        var isByoTier = string.Equals(effectiveTier, AccessModeResolver.ProByo, StringComparison.OrdinalIgnoreCase);

        if (!isFreeTier && !hasPremiumManagedLane && !(isByoTier && allowPaidSessionExtension))
        {
            throw new BackendValidationException("Managed AI is available only for Free and Premium tiers.");
        }
    }

    private ManagedProviderCredentialRecord[] GetEnabledProviderCredentials(string providerId)
    {
        var now = DateTime.UtcNow;
        var enabled = _credentials.ListByProvider(providerId)
            .Where(item => item.IsEnabled)
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.UpdatedAtUtc)
            .ToArray();

        if (enabled.Length == 0)
        {
            throw new BackendValidationException($"No managed credentials are configured for {providerId}.");
        }

        var healthy = enabled
            .Where(item => !item.CooldownUntilUtc.HasValue || item.CooldownUntilUtc.Value <= now)
            .Take(ProviderResiliencePolicy.ManagedBackendMaxAttempts)
            .ToArray();
        if (healthy.Length > 0) return healthy;

        var retryAfter = Math.Max(1, (int)Math.Ceiling(enabled.Min(item => item.CooldownUntilUtc!.Value).Subtract(now).TotalSeconds));
        throw new ManagedAiProviderException(
            "provider_credentials_cooling_down", true, retryAfterSeconds: retryAfter);
    }

    private async Task<string> GenerateProviderResponseWithFallbackAsync(
        string providerId,
        string modelId,
        IReadOnlyList<DesktopAiChatMessageDto> messages,
        IReadOnlyList<string>? imagesBase64,
        CancellationToken cancellationToken,
        int maxOutputTokens = 64)
    {
        var providerCredentials = GetEnabledProviderCredentials(providerId);
        var operationId = Guid.NewGuid().ToString("N");
        Exception? lastError = null;

        for (var credentialIndex = 0; credentialIndex < providerCredentials.Length; credentialIndex++)
        {
            var credential = providerCredentials[credentialIndex];
            var attempt = credentialIndex + 1;
            try
            {
                LogProviderAttempt(providerId, modelId, string.Empty, operationId, string.Empty, attempt, credentialIndex + 1);
                var apiKey = _protector.Unprotect(credential.EncryptedApiKey);
                var result = await GenerateProviderResponseAsync(
                    providerId,
                    modelId,
                    messages,
                    imagesBase64,
                    apiKey,
                    cancellationToken,
                    maxOutputTokens);
                RecordCredentialSuccess(credential);
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not BackendValidationException)
            {
                lastError = ex;
                var failure = ProviderResiliencePolicy.Classify(ex);
                RecordCredentialFailure(credential, failure);
                if (!ProviderResiliencePolicy.CanRetry(failure, attempt, hasOutput: false))
                    throw ManagedAiProviderException.FromFailure(ex);
                LogProviderRetry(providerId, modelId, string.Empty, operationId, string.Empty, attempt + 1, failure);
            }
        }

        throw lastError ?? new InvalidOperationException("Managed AI request failed.");
    }

    private void RecordCredentialFailure(ManagedProviderCredentialRecord credential, ProviderFailureDecision failure)
    {
        if (!failure.CanRotateCredential || failure.Cooldown <= TimeSpan.Zero) return;
        try
        {
            _credentials.RecordFailure(credential.CredentialId, failure.ErrorCode, DateTime.UtcNow.Add(failure.Cooldown));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "managed_ai_credential_health_write_failed service={Service} component={Component} event={Event} provider={Provider} error_code={ErrorCode} outcome={Outcome}",
                "phantom-windows-app-backend", "managed_ai", "credential_health_write_failed", credential.ProviderId,
                "credential_health_store_failed", "degraded");
        }
    }

    private void RecordCredentialSuccess(ManagedProviderCredentialRecord credential)
    {
        if (!credential.CooldownUntilUtc.HasValue && credential.ConsecutiveFailureCount == 0 && string.IsNullOrEmpty(credential.LastFailureCode)) return;
        try { _credentials.RecordSuccess(credential.CredentialId); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "managed_ai_credential_health_write_failed service={Service} component={Component} event={Event} provider={Provider} error_code={ErrorCode} outcome={Outcome}",
                "phantom-windows-app-backend", "managed_ai", "credential_health_write_failed", credential.ProviderId,
                "credential_health_store_failed", "degraded");
        }
    }

    private void LogProviderAttempt(string provider, string model, string requestId, string operationId, string turnId, int attempt, int credentialSlot)
    {
        _logger.LogInformation(
            "managed_ai_attempt service={Service} component={Component} event={Event} request_id={RequestId} operation_id={OperationId} turn_id={TurnId} provider={Provider} model={Model} execution_lane={ExecutionLane} attempt={Attempt} credential_slot={CredentialSlot} outcome={Outcome}",
            "phantom-windows-app-backend", "managed_ai", "provider_attempt_started", requestId, operationId, turnId,
            provider, model, "managed", attempt, credentialSlot, "started");
    }

    private void LogProviderRetry(string provider, string model, string requestId, string operationId, string turnId, int nextAttempt, ProviderFailureDecision failure)
    {
        _logger.LogWarning(
            "managed_ai_retry service={Service} component={Component} event={Event} request_id={RequestId} operation_id={OperationId} turn_id={TurnId} provider={Provider} model={Model} execution_lane={ExecutionLane} attempt={Attempt} error_code={ErrorCode} failure_class={FailureClass} cooldown_ms={CooldownMs} outcome={Outcome}",
            "phantom-windows-app-backend", "managed_ai", "provider_retry_started", requestId, operationId, turnId,
            provider, model, "managed", nextAttempt, failure.ErrorCode, failure.Kind.ToString().ToLowerInvariant(),
            failure.Cooldown.TotalMilliseconds, "retry");
        _logger.LogWarning(
            "managed_ai_rotation service={Service} component={Component} event={Event} request_id={RequestId} operation_id={OperationId} turn_id={TurnId} provider={Provider} model={Model} execution_lane={ExecutionLane} attempt={Attempt} key_rotated={KeyRotated} outcome={Outcome}",
            "phantom-windows-app-backend", "managed_ai", "provider_rotated", requestId, operationId, turnId,
            provider, model, "managed", nextAttempt, true, "retry");
    }

    private async Task StreamProviderAsync(SseDeltaWriter streamWriter, DesktopAiChatRequestDto request, string apiKey, CancellationToken cancellationToken)
    {
        RunOutputBudgetSelfCheck();
        var reasoningPlan = ReasoningPlanner.For(request.Messages, request.QuestionType);
        var outputBudget = reasoningPlan.OutputTokens;
        var includeThinking = ReasoningPlanner.SupportsNativeThinking(request.Provider, request.Model)
            && reasoningPlan.Effort != "low";
        _logger.LogInformation(
            "managed_ai_dispatch service={Service} component={Component} event={Event} request_id={RequestId} operation_id={OperationId} turn_id={TurnId} provider={Provider} model={Model} execution_lane={ExecutionLane} max_output_tokens={MaxOutputTokens} reasoning_effort={ReasoningEffort} native_thinking={NativeThinking} estimated_input_tokens={EstimatedInputTokens} recent_turn_count={RecentTurnCount} image_present={ImagePresent}",
            "phantom-windows-app-backend", "managed_ai", "provider_request_started", request.RequestId, request.RequestId, request.TurnId,
            request.Provider, request.Model, "managed", outputBudget, reasoningPlan.Effort, includeThinking,
            request.Messages.Sum(message => Math.Max(1, (message.Content?.Length ?? 0) / 4)),
            request.Messages.Count, request.GetNormalizedImages().Count > 0);
        switch (request.Provider)
        {
            case ManagedAiCatalog.ChatGpt:
                await StreamOpenAiCompatibleAsync(
                    streamWriter,
                    "https://api.openai.com/v1/chat/completions",
                    BuildOpenAiMessages(request.Messages, request.GetNormalizedImages(), mistralImageUrl: false),
                    request.Model,
                    apiKey,
                    cancellationToken,
                    reasoningPlan,
                    includeThinking);
                return;
            case ManagedAiCatalog.Mistral:
                await StreamOpenAiCompatibleAsync(
                    streamWriter,
                    "https://api.mistral.ai/v1/chat/completions",
                    BuildOpenAiMessages(request.Messages, request.GetNormalizedImages(), mistralImageUrl: true),
                    request.Model,
                    apiKey,
                    cancellationToken,
                    reasoningPlan,
                    includeThinking);
                return;
            case ManagedAiCatalog.Groq:
                await StreamOpenAiCompatibleAsync(
                    streamWriter,
                    "https://api.groq.com/openai/v1/chat/completions",
                    BuildOpenAiMessages(request.Messages, request.GetNormalizedImages(), mistralImageUrl: false),
                    request.Model,
                    apiKey,
                    cancellationToken,
                    reasoningPlan,
                    includeThinking);
                return;
            case ManagedAiCatalog.Claude:
                await StreamClaudeAsync(streamWriter, request, apiKey, reasoningPlan, includeThinking, cancellationToken);
                return;
            case ManagedAiCatalog.Gemini:
                await StreamGeminiAsync(streamWriter, request, apiKey, reasoningPlan, includeThinking, cancellationToken);
                return;
            case ManagedAiCatalog.Nvidia:
                await StreamOpenAiCompatibleAsync(
                    streamWriter,
                    "https://integrate.api.nvidia.com/v1/chat/completions",
                    BuildOpenAiMessages(request.Messages, request.GetNormalizedImages(), mistralImageUrl: false),
                    request.Model,
                    apiKey,
                    cancellationToken,
                    reasoningPlan,
                    includeThinking);
                return;
            case ManagedAiCatalog.OpenRouter:
                await StreamOpenAiCompatibleAsync(
                    streamWriter,
                    ManagedAiCatalog.OpenRouterChatCompletionsUrl,
                    BuildOpenAiMessages(request.Messages, request.GetNormalizedImages(), mistralImageUrl: false),
                    request.Model,
                    apiKey,
                    cancellationToken,
                    reasoningPlan,
                    includeThinking,
                    ManagedAiCatalog.ApplyOpenRouterHeaders);
                return;
            default:
                throw new BackendValidationException("Unsupported managed provider.");
        }
    }

    private async Task<string> GenerateProviderResponseAsync(
        string providerId,
        string modelId,
        IReadOnlyList<DesktopAiChatMessageDto> messages,
        IReadOnlyList<string>? imagesBase64,
        string apiKey,
        CancellationToken cancellationToken,
        int maxOutputTokens)
    {
        var reasoningPlan = ReasoningPlanner.For(messages);
        var includeThinking = ReasoningPlanner.SupportsNativeThinking(providerId, modelId)
            && reasoningPlan.Effort != "low";
        return providerId switch
        {
            ManagedAiCatalog.ChatGpt => await GenerateOpenAiCompatibleResponseAsync(
                "https://api.openai.com/v1/chat/completions",
                BuildOpenAiMessages(messages, imagesBase64, mistralImageUrl: false),
                modelId,
                apiKey,
                cancellationToken,
                reasoningPlan,
                includeThinking),
            ManagedAiCatalog.Mistral => await GenerateOpenAiCompatibleResponseAsync(
                "https://api.mistral.ai/v1/chat/completions",
                BuildOpenAiMessages(messages, imagesBase64, mistralImageUrl: true),
                modelId,
                apiKey,
                cancellationToken,
                reasoningPlan,
                includeThinking),
            ManagedAiCatalog.Groq => await GenerateOpenAiCompatibleResponseAsync(
                "https://api.groq.com/openai/v1/chat/completions",
                BuildOpenAiMessages(messages, imagesBase64, mistralImageUrl: false),
                modelId,
                apiKey,
                cancellationToken,
                reasoningPlan,
                includeThinking),
            ManagedAiCatalog.Claude => await GenerateClaudeResponseAsync(modelId, messages, imagesBase64, apiKey, cancellationToken, reasoningPlan, includeThinking),
            ManagedAiCatalog.Gemini => await GenerateGeminiResponseAsync(modelId, messages, imagesBase64, apiKey, cancellationToken, reasoningPlan, includeThinking),
            ManagedAiCatalog.Nvidia => await GenerateOpenAiCompatibleResponseAsync(
                "https://integrate.api.nvidia.com/v1/chat/completions",
                BuildOpenAiMessages(messages, imagesBase64, mistralImageUrl: false),
                modelId,
                apiKey,
                cancellationToken,
                reasoningPlan,
                includeThinking),
            ManagedAiCatalog.OpenRouter => await GenerateOpenAiCompatibleResponseAsync(
                ManagedAiCatalog.OpenRouterChatCompletionsUrl,
                BuildOpenAiMessages(messages, imagesBase64, mistralImageUrl: false),
                modelId,
                apiKey,
                cancellationToken,
                reasoningPlan,
                includeThinking,
                ManagedAiCatalog.ApplyOpenRouterHeaders),
            _ => throw new BackendValidationException("Unsupported managed provider.")
        };
    }

    private async Task StreamOpenAiCompatibleAsync(
        SseDeltaWriter streamWriter,
        string url,
        object[] messages,
        string model,
        string apiKey,
        CancellationToken cancellationToken,
        ReasoningPlan reasoningPlan,
        bool includeThinking,
        Action<HttpRequestHeaders>? configureHeaders = null)
    {
        using var response = await SendOpenAiCompatibleAsync(
            url,
            messages,
            model,
            apiKey,
            stream: true,
            cancellationToken,
            configureHeaders,
            reasoningPlan,
            includeThinking);
        streamWriter.MarkProviderHeaders();
        if (!response.IsSuccessStatusCode)
        {
            await ThrowProviderHttpErrorAsync(response, cancellationToken);
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var sawDone = false;
        string? finishReason = null;
        string? streamError = null;
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var data = ReadSseData(line);
            if (data is null)
            {
                continue;
            }

            if (data == "[DONE]")
            {
                sawDone = true;
                break;
            }

            JsonDocument json;
            try
            {
                json = JsonDocument.Parse(data);
            }
            catch (JsonException)
            {
                continue;
            }

            using (json)
            {
                var error = ReadProviderErrorMessage(json.RootElement);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    streamError = error;
                    break;
                }

                if (!json.RootElement.TryGetProperty("choices", out var choices)
                    || choices.GetArrayLength() == 0)
                {
                    continue;
                }

                var choice = choices[0];
                if (choice.TryGetProperty("finish_reason", out var finishElement) && finishElement.ValueKind == JsonValueKind.String)
                {
                    finishReason = finishElement.GetString();
                }
                if (!choice.TryGetProperty("delta", out var delta))
                {
                    continue;
                }

                var deltaText = ReadOpenAiDeltaText(delta);
                if (string.IsNullOrEmpty(deltaText))
                {
                    continue;
                }

                await streamWriter.AppendAsync(deltaText, cancellationToken);
            }
        }

        if (!string.IsNullOrWhiteSpace(streamError))
            throw new InvalidOperationException(streamError);
        if (string.Equals(finishReason, "error", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("provider_error");
        if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
        {
            if (!streamWriter.HasWritten)
                throw new InvalidOperationException("provider_output_truncated");
            streamWriter.Complete("length");
            return;
        }
        if (!sawDone && !string.Equals(finishReason, "stop", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("provider_stream_incomplete");
        streamWriter.Complete(finishReason ?? "done");
    }

    private async Task<string> GenerateOpenAiCompatibleResponseAsync(
        string url,
        object[] messages,
        string model,
        string apiKey,
        CancellationToken cancellationToken,
        ReasoningPlan reasoningPlan,
        bool includeThinking,
        Action<HttpRequestHeaders>? configureHeaders = null)
    {
        using var response = await SendOpenAiCompatibleAsync(
            url,
            messages,
            model,
            apiKey,
            stream: false,
            cancellationToken,
            configureHeaders,
            reasoningPlan,
            includeThinking);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowProviderHttpErrorAsync(response, cancellationToken);
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var error = ReadProviderErrorMessage(document.RootElement);
        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException(error);
        }

        if (!document.RootElement.TryGetProperty("choices", out var choices)
            || choices.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var choice = choices[0];
        if (!choice.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        return ReadOpenAiContent(content);
    }

    private async Task<HttpResponseMessage> SendOpenAiCompatibleAsync(
        string url,
        object[] messages,
        string model,
        string apiKey,
        bool stream,
        CancellationToken cancellationToken,
        Action<HttpRequestHeaders>? configureHeaders,
        ReasoningPlan reasoningPlan,
        bool includeThinking)
    {
        async Task<HttpResponseMessage> SendAsync(bool withThinking)
        {
            var payload = SerializeOpenAiCompatiblePayload(model, messages, stream, reasoningPlan, withThinking);
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            configureHeaders?.Invoke(request.Headers);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            return await HttpClient.SendAsync(
                request,
                stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                cancellationToken);
        }

        var response = await SendAsync(withThinking: includeThinking);
        if (includeThinking && response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "managed_ai_reasoning_retry service={Service} component={Component} event={Event} model={Model} error_detail={ErrorDetail} outcome={Outcome}",
                "phantom-windows-app-backend", "managed_ai", "native_thinking_rejected", model, TruncateForLog(body), "retry");
            response.Dispose();
            response = await SendAsync(withThinking: false);
        }

        return response;
    }

    private static string SerializeOpenAiCompatiblePayload(
        string model,
        object[] messages,
        bool stream,
        ReasoningPlan reasoningPlan,
        bool includeThinking)
    {
        var outputTokens = reasoningPlan.OutputTokens;
        if (includeThinking)
        {
            return JsonSerializer.Serialize(new
            {
                model,
                messages,
                max_tokens = outputTokens,
                stream,
                reasoning = new { exclude = true, effort = reasoningPlan.Effort }
            });
        }

        return JsonSerializer.Serialize(new
        {
            model,
            messages,
            max_tokens = outputTokens,
            stream
        });
    }

    private async Task ThrowProviderHttpErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "managed_ai_provider_http_error service={Service} component={Component} event={Event} status_code={StatusCode} error_detail={ErrorDetail} outcome={Outcome}",
            "phantom-windows-app-backend", "managed_ai", "provider_http_error", (int)response.StatusCode, TruncateForLog(body), "error");
        throw ManagedAiProviderException.FromStatusCode((int)response.StatusCode);
    }

    private static string? ReadSseData(string line)
    {
        if (line.StartsWith("data: ", StringComparison.Ordinal))
        {
            return line[6..].Trim();
        }

        return line.StartsWith("data:", StringComparison.Ordinal) ? line[5..].Trim() : null;
    }

    private static string? ReadProviderErrorMessage(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error) || error.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (error.ValueKind == JsonValueKind.String)
        {
            return error.GetString();
        }

        if (error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.String)
        {
            return message.GetString();
        }

        return error.ToString();
    }

    private static string ReadOpenAiDeltaText(JsonElement delta)
    {
        if (delta.TryGetProperty("content", out var content))
        {
            var text = ReadOpenAiContent(content);
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private async Task<HttpResponseMessage> SendClaudeAsync(
        string model,
        string systemPrompt,
        object[] apiMessages,
        string apiKey,
        bool stream,
        ReasoningPlan reasoningPlan,
        bool includeThinking,
        CancellationToken cancellationToken)
    {
        async Task<HttpResponseMessage> SendAsync(bool withThinking)
        {
            var maxTokens = Math.Max(reasoningPlan.OutputTokens, reasoningPlan.ClaudeThinkingTokens + 512);
            var payload = withThinking && reasoningPlan.ClaudeThinkingTokens > 0
                ? JsonSerializer.Serialize(new
                {
                    model,
                    max_tokens = maxTokens,
                    system = systemPrompt,
                    messages = apiMessages,
                    stream,
                    thinking = new { type = "enabled", budget_tokens = reasoningPlan.ClaudeThinkingTokens }
                })
                : JsonSerializer.Serialize(new
                {
                    model,
                    max_tokens = maxTokens,
                    system = systemPrompt,
                    messages = apiMessages,
                    stream
                });
            var outbound = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            outbound.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            outbound.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            outbound.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            return await HttpClient.SendAsync(
                outbound,
                stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                cancellationToken);
        }

        var response = await SendAsync(withThinking: includeThinking);
        if (includeThinking && reasoningPlan.ClaudeThinkingTokens > 0 && response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "managed_ai_reasoning_retry service={Service} component={Component} event={Event} model={Model} error_detail={ErrorDetail} outcome={Outcome}",
                "phantom-windows-app-backend", "managed_ai", "native_thinking_rejected", model, TruncateForLog(body), "retry");
            response.Dispose();
            response = await SendAsync(withThinking: false);
        }

        return response;
    }

    private async Task StreamClaudeAsync(
        SseDeltaWriter streamWriter,
        DesktopAiChatRequestDto request,
        string apiKey,
        ReasoningPlan reasoningPlan,
        bool includeThinking,
        CancellationToken cancellationToken)
    {
        var systemPrompt = request.Messages.FirstOrDefault(item => string.Equals(item.Role, "system", StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty;
        var filteredMessages = request.Messages
            .Where(item => !string.Equals(item.Role, "system", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.Content))
            .ToArray();
        var apiMessages = BuildClaudeMessages(filteredMessages, request.GetNormalizedImages());
        using var response = await SendClaudeAsync(
            request.Model, systemPrompt, apiMessages, apiKey, stream: true, reasoningPlan, includeThinking, cancellationToken);
        streamWriter.MarkProviderHeaders();
        if (!response.IsSuccessStatusCode)
        {
            await ThrowProviderHttpErrorAsync(response, cancellationToken);
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var sawMessageStop = false;
        string? stopReason = null;
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[6..];
            if (data == "[DONE]")
            {
                break;
            }

            using var json = JsonDocument.Parse(data);
            if (!json.RootElement.TryGetProperty("type", out var typeElement))
            {
                continue;
            }
            var eventType = typeElement.GetString();
            if (eventType == "message_stop") { sawMessageStop = true; break; }
            if (eventType == "message_delta"
                && json.RootElement.TryGetProperty("delta", out var messageDelta)
                && messageDelta.TryGetProperty("stop_reason", out var stopElement))
            {
                stopReason = stopElement.GetString();
                continue;
            }
            if (eventType != "content_block_delta") continue;

            if (!json.RootElement.TryGetProperty("delta", out var delta)
                || !delta.TryGetProperty("text", out var textElement))
            {
                continue;
            }

            var deltaText = textElement.GetString();
            if (string.IsNullOrEmpty(deltaText))
            {
                continue;
            }

            await streamWriter.AppendAsync(deltaText, cancellationToken);
        }
        if (string.Equals(stopReason, "max_tokens", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("provider_output_truncated");
        if (!sawMessageStop) throw new InvalidOperationException("provider_stream_incomplete");
        streamWriter.Complete(stopReason ?? "stop");
    }

    private async Task<string> GenerateClaudeResponseAsync(
        string modelId,
        IReadOnlyList<DesktopAiChatMessageDto> messages,
        IReadOnlyList<string>? imagesBase64,
        string apiKey,
        CancellationToken cancellationToken,
        ReasoningPlan reasoningPlan,
        bool includeThinking)
    {
        var systemPrompt = messages.FirstOrDefault(item => string.Equals(item.Role, "system", StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty;
        var filteredMessages = messages
            .Where(item => !string.Equals(item.Role, "system", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.Content))
            .ToArray();
        using var response = await SendClaudeAsync(
            modelId,
            systemPrompt,
            BuildClaudeMessages(filteredMessages, imagesBase64),
            apiKey,
            stream: false,
            reasoningPlan,
            includeThinking,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowProviderHttpErrorAsync(response, cancellationToken);
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            content.EnumerateArray()
                .Where(item =>
                    item.TryGetProperty("type", out var typeElement)
                    && string.Equals(typeElement.GetString(), "text", StringComparison.OrdinalIgnoreCase)
                    && item.TryGetProperty("text", out var textElement)
                    && !string.IsNullOrWhiteSpace(textElement.GetString()))
                .Select(item => item.GetProperty("text").GetString()!.Trim()));
    }

    private async Task StreamGeminiAsync(
        SseDeltaWriter streamWriter,
        DesktopAiChatRequestDto request,
        string apiKey,
        ReasoningPlan reasoningPlan,
        bool includeThinking,
        CancellationToken cancellationToken)
    {
        var contents = BuildGeminiContents(request.Messages, request.GetNormalizedImages());
        using var response = await SendGeminiAsync(
            request.Model, contents, apiKey, stream: true, reasoningPlan, includeThinking, 0.7, cancellationToken);
        streamWriter.MarkProviderHeaders();
        if (!response.IsSuccessStatusCode)
        {
            await ThrowProviderHttpErrorAsync(response, cancellationToken);
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        string? finishReason = null;
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[6..];
            using var json = JsonDocument.Parse(data);
            if (!json.RootElement.TryGetProperty("candidates", out var candidates)
                || candidates.GetArrayLength() == 0)
            {
                continue;
            }

            var candidate = candidates[0];
            if (candidate.TryGetProperty("finishReason", out var finishElement) && finishElement.ValueKind == JsonValueKind.String)
            {
                finishReason = finishElement.GetString();
            }
            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.GetArrayLength() == 0)
            {
                continue;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                if (!part.TryGetProperty("text", out var textElement))
                {
                    continue;
                }

                var deltaText = textElement.GetString();
                if (string.IsNullOrEmpty(deltaText))
                {
                    continue;
                }

                await streamWriter.AppendAsync(deltaText, cancellationToken);
            }
        }
        if (string.Equals(finishReason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("provider_output_truncated");
        if (!string.Equals(finishReason, "STOP", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("provider_stream_incomplete");
        streamWriter.Complete(finishReason ?? "unknown");
    }

    private async Task<HttpResponseMessage> SendGeminiAsync(
        string modelId,
        object contents,
        string apiKey,
        bool stream,
        ReasoningPlan reasoningPlan,
        bool includeThinking,
        double temperature,
        CancellationToken cancellationToken)
    {
        async Task<HttpResponseMessage> SendAsync(bool withThinking)
        {
            var payload = withThinking && reasoningPlan.GeminiThinkingTokens > 0
                ? JsonSerializer.Serialize(new
                {
                    contents,
                    generationConfig = new
                    {
                        maxOutputTokens = reasoningPlan.OutputTokens,
                        temperature,
                        thinkingConfig = new
                        {
                            thinkingBudget = reasoningPlan.GeminiThinkingTokens,
                            includeThoughts = false
                        }
                    }
                })
                : JsonSerializer.Serialize(new
                {
                    contents,
                    generationConfig = new
                    {
                        maxOutputTokens = reasoningPlan.OutputTokens,
                        temperature
                    }
                });
            var url = stream
                ? $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(modelId)}:streamGenerateContent?key={Uri.EscapeDataString(apiKey)}&alt=sse"
                : $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(modelId)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
            var outbound = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            return await HttpClient.SendAsync(
                outbound,
                stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                cancellationToken);
        }

        var response = await SendAsync(withThinking: includeThinking);
        if (includeThinking && reasoningPlan.GeminiThinkingTokens > 0 && response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "managed_ai_reasoning_retry service={Service} component={Component} event={Event} model={Model} error_detail={ErrorDetail} outcome={Outcome}",
                "phantom-windows-app-backend", "managed_ai", "native_thinking_rejected", modelId, TruncateForLog(body), "retry");
            response.Dispose();
            response = await SendAsync(withThinking: false);
        }

        return response;
    }

    private async Task<string> GenerateGeminiResponseAsync(
        string modelId,
        IReadOnlyList<DesktopAiChatMessageDto> messages,
        IReadOnlyList<string>? imagesBase64,
        string apiKey,
        CancellationToken cancellationToken,
        ReasoningPlan reasoningPlan,
        bool includeThinking)
    {
        using var response = await SendGeminiAsync(
            modelId,
            BuildGeminiContents(messages, imagesBase64),
            apiKey,
            stream: false,
            reasoningPlan,
            includeThinking,
            0.2,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowProviderHttpErrorAsync(response, cancellationToken);
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("candidates", out var candidates)
            || candidates.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            parts.EnumerateArray()
                .Where(item => item.TryGetProperty("text", out var textElement) && !string.IsNullOrWhiteSpace(textElement.GetString()))
                .Select(item => item.GetProperty("text").GetString()!.Trim()));
    }

    private static object[] BuildOpenAiMessages(IReadOnlyList<DesktopAiChatMessageDto> messages, IReadOnlyList<string>? imagesBase64, bool mistralImageUrl)
    {
        var images = NormalizeImages(imagesBase64);
        var filtered = messages
            .Where(item => !string.IsNullOrWhiteSpace(item.Content))
            .ToArray();

        var lastUserIndex = -1;
        for (var i = filtered.Length - 1; i >= 0; i--)
        {
            if (string.Equals(filtered[i].Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                lastUserIndex = i;
                break;
            }
        }

        return filtered
            .Select((item, index) =>
            {
                var attachImages = index == lastUserIndex
                    && string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase)
                    && images.Count > 0;

                if (!attachImages)
                {
                    return (object)new
                    {
                        role = item.Role,
                        content = item.Content
                    };
                }

                var content = new List<object> { new { type = "text", text = item.Content } };
                foreach (var image in images)
                {
                    content.Add(mistralImageUrl
                        ? (object)new { type = "image_url", image_url = $"data:image/png;base64,{image}" }
                        : (object)new { type = "image_url", image_url = new { url = $"data:image/png;base64,{image}" } });
                }

                return new
                {
                    role = "user",
                    content = content.ToArray()
                };
            })
            .ToArray();
    }

    private static object[] BuildClaudeMessages(IReadOnlyList<DesktopAiChatMessageDto> filteredMessages, IReadOnlyList<string>? imagesBase64)
    {
        var images = NormalizeImages(imagesBase64);
        return filteredMessages
            .Select((item, index) =>
            {
                var isLastUser = string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase)
                    && index == filteredMessages.Count - 1;

                if (isLastUser && images.Count > 0)
                {
                    var content = new List<object>();
                    foreach (var image in images)
                    {
                        content.Add(new
                        {
                            type = "image",
                            source = new
                            {
                                type = "base64",
                                media_type = "image/png",
                                data = image
                            }
                        });
                    }
                    content.Add(new { type = "text", text = item.Content });
                    return (object)new
                    {
                        role = "user",
                        content = content.ToArray()
                    };
                }

                return (object)new
                {
                    role = item.Role,
                    content = item.Content
                };
            })
            .ToArray();
    }

    private static object[] BuildGeminiContents(IReadOnlyList<DesktopAiChatMessageDto> messages, IReadOnlyList<string>? imagesBase64)
    {
        var images = NormalizeImages(imagesBase64);
        var systemPrompt = messages.FirstOrDefault(item => string.Equals(item.Role, "system", StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty;
        var filtered = messages.Where(item => !string.Equals(item.Role, "system", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.Content)).ToArray();
        var items = new List<object>();

        for (var i = 0; i < filtered.Length; i++)
        {
            var item = filtered[i];
            var role = string.Equals(item.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user";
            var text = i == 0 && !string.IsNullOrWhiteSpace(systemPrompt)
                ? $"{systemPrompt}\n\n{item.Content}"
                : item.Content;

            var attachImages = i == filtered.Length - 1
                && role == "user"
                && images.Count > 0;

            if (attachImages)
            {
                var parts = new List<object> { new { text } };
                foreach (var image in images)
                {
                    parts.Add(new
                    {
                        inline_data = new
                        {
                            mime_type = "image/png",
                            data = image
                        }
                    });
                }

                items.Add(new
                {
                    role,
                    parts = parts.ToArray()
                });
            }
            else
            {
                items.Add(new
                {
                    role,
                    parts = new object[]
                    {
                        new { text }
                    }
                });
            }
        }

        return items.ToArray();
    }

    private static IReadOnlyList<string> NormalizeImages(IReadOnlyList<string>? imagesBase64)
    {
        if (imagesBase64 == null || imagesBase64.Count == 0)
        {
            return Array.Empty<string>();
        }

        return imagesBase64
            .Where(image => !string.IsNullOrWhiteSpace(image))
            .Select(image => image.Trim())
            .Take(DesktopAiChatRequestDto.MaxAttachedImages)
            .ToArray();
    }

    private static string ReadOpenAiContent(JsonElement content)
    {
        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(
                " ",
                content.EnumerateArray()
                    .Where(item =>
                        item.TryGetProperty("text", out var textElement)
                        && !string.IsNullOrWhiteSpace(textElement.GetString()))
                    .Select(item => item.GetProperty("text").GetString()!.Trim())),
            _ => string.Empty
        };
    }

    private static string NormalizeResponseText(string responseText)
    {
        return string.IsNullOrWhiteSpace(responseText) ? "[empty response]" : responseText.Trim();
    }

    private static string TruncateForLog(string? value, int maxLength = 300)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim().ReplaceLineEndings(" ");
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static int ToLatencyMs(long elapsedMilliseconds)
    {
        return elapsedMilliseconds > int.MaxValue ? int.MaxValue : (int)Math.Max(0, elapsedMilliseconds);
    }

    private static string ClassifyAdminFailure(Exception ex)
    {
        return LooksLikeNonChatCapable(ex.Message) ? "not_chat_capable" : "failed";
    }

    private static bool? InferChatCapability(Exception ex)
    {
        return LooksLikeNonChatCapable(ex.Message) ? false : null;
    }

    private static bool LooksLikeNonChatCapable(string message)
    {
        var normalized = message.ToLowerInvariant();
        return normalized.Contains("unsupported")
            || normalized.Contains("not supported")
            || normalized.Contains("does not support")
            || normalized.Contains("no such model")
            || normalized.Contains("unknown model")
            || normalized.Contains("invalid model")
            || normalized.Contains("model_not_found")
            || (normalized.Contains("404") && normalized.Contains("model"))
            || normalized.Contains("chat/completions")
            || normalized.Contains("generatecontent")
            || normalized.Contains("messages api");
    }

    private static string GetSingleLineMessage(Exception ex)
    {
        var message = ex.Message?.Trim() ?? "Managed AI request failed.";
        var newlineIndex = message.IndexOfAny(['\r', '\n']);
        return newlineIndex >= 0 ? message[..newlineIndex].Trim() : message;
    }

    [Conditional("DEBUG")]
    private static void RunOutputBudgetSelfCheck()
    {
        static DesktopAiChatMessageDto User(string content) => new() { Role = "user", Content = content };
        Debug.Assert(ReasoningPlanner.For(new[] { User("Why?") }).Effort == "low");
        Debug.Assert(ReasoningPlanner.For(new[] { User("Design a highly available payment system") }).Effort == "high");
        Debug.Assert(ReasoningPlanner.For(new[] { User("Write code for LRU cache") }).Effort == "high");
        Debug.Assert(ReasoningPlanner.For(new[] { User("What is CAP theorem?") }).Effort == "medium");
        Debug.Assert(ReasoningPlanner.For(new[] { User("Tell me about a challenge") }, "coding").Effort == "high");
        Debug.Assert(ReasoningPlanner.SupportsNativeThinking("OpenRouter", "z-ai/glm-5.3-flash"));
        Debug.Assert(ReasoningPlanner.SupportsNativeThinking("Claude", "claude-sonnet-4-5"));
        Debug.Assert(!ReasoningPlanner.SupportsNativeThinking("ChatGPT", "gpt-4o-mini"));
    }

    private static async Task WriteSseJsonAsync(HttpResponse response, object payload, CancellationToken cancellationToken)
    {
        await WriteSseDataAsync(response, JsonSerializer.Serialize(payload), cancellationToken);
    }

    private static async Task WriteSseDataAsync(HttpResponse response, string data, CancellationToken cancellationToken)
    {
        await response.WriteAsync($"data: {data}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private sealed class SseDeltaWriter
    {
        private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(35);
        private readonly HttpResponse _response;
        private readonly Action<string> _mark;
        private readonly StringBuilder _pending = new();
        private readonly Stopwatch _sinceFlush = Stopwatch.StartNew();
        private bool _providerHeadersMarked;
        private bool _firstTokenMarked;

        private readonly Action _firstToken;

        public SseDeltaWriter(HttpResponse response, Action<string> mark, Action firstToken)
        {
            _response = response;
            _mark = mark;
            _firstToken = firstToken;
        }

        public bool HasWritten { get; private set; }
        public int ChunkCount { get; private set; }
        public int BufferedCharacters { get; private set; }
        public int FlushCount { get; private set; }
        public string FinishReason { get; private set; } = "unknown";

        public void MarkProviderHeaders()
        {
            if (_providerHeadersMarked) return;
            _providerHeadersMarked = true;
            _mark("provider_headers_received");
        }

        public async Task AppendAsync(string delta, CancellationToken cancellationToken)
        {
            ChunkCount++;
            BufferedCharacters += delta.Length;
            if (!_firstTokenMarked)
            {
                _firstTokenMarked = true;
                _firstToken();
                _mark("first_upstream_token");
                await WriteSseJsonAsync(_response, new { delta }, cancellationToken);
                HasWritten = true;
                FlushCount++;
                _mark("first_backend_sse_write");
                _sinceFlush.Restart();
                return;
            }

            _pending.Append(delta);
            if (_sinceFlush.Elapsed >= CoalesceWindow)
            {
                await FlushAsync(cancellationToken);
            }
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (_pending.Length == 0) return;
            var delta = _pending.ToString();
            _pending.Clear();
            await WriteSseJsonAsync(_response, new { delta }, cancellationToken);
            HasWritten = true;
            FlushCount++;
            _sinceFlush.Restart();
        }

        public void Complete(string finishReason)
        {
            FinishReason = string.IsNullOrWhiteSpace(finishReason) ? "unknown" : finishReason.ToLowerInvariant();
        }
    }
}
