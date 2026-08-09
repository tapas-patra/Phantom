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

        var record = existing ?? new ManagedProviderCredentialRecord
        {
            CredentialId = string.IsNullOrWhiteSpace(request.CredentialId)
                ? $"managed-key-{Guid.NewGuid():N}"
                : request.CredentialId,
            CreatedAtUtc = now
        };

        record.ProviderId = request.ProviderId.Trim();
        record.Label = string.IsNullOrWhiteSpace(request.Label) ? request.ProviderId.Trim() : request.Label.Trim();
        record.EncryptedApiKey = _protector.Protect(request.ApiKey.Trim());
        record.IsEnabled = request.IsEnabled;
        record.Priority = request.Priority;
        record.UpdatedAtUtc = now;

        _credentials.Save(record);
        return new ManagedAiProviderKeyDto
        {
            CredentialId = record.CredentialId,
            ProviderId = record.ProviderId,
            Label = record.Label,
            IsEnabled = record.IsEnabled,
            Priority = record.Priority,
            UpdatedAtUtc = record.UpdatedAtUtc
        };
    }

    public void DeleteCredential(string credentialId)
    {
        if (string.IsNullOrWhiteSpace(credentialId))
        {
            throw new BackendValidationException("credentialId is required.");
        }

        _credentials.Delete(credentialId.Trim());
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
        LogTiming(request, "provider_request_started", stopwatch);
        EnsureManagedAccess(account, request.AllowPaidSessionExtension);

        if (string.IsNullOrWhiteSpace(request.Provider) || !ManagedAiCatalog.IsAllowedProvider(request.Provider))
        {
            throw new BackendValidationException("Unsupported managed provider.");
        }

        if (string.IsNullOrWhiteSpace(request.Model) || !_catalogService.IsAllowedModel(request.Provider, request.Model))
        {
            throw new BackendValidationException("Unsupported managed model.");
        }

        if (!string.IsNullOrWhiteSpace(request.ImageBase64)
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
        var streamWriter = new SseDeltaWriter(response, milestone => LogTiming(request, milestone, stopwatch));

        Exception? lastError = null;
        foreach (var credential in providerCredentials)
        {
            try
            {
                var apiKey = _protector.Unprotect(credential.EncryptedApiKey);
                await StreamProviderAsync(streamWriter, request, apiKey, cancellationToken);
                await streamWriter.FlushAsync(cancellationToken);
                await WriteSseDataAsync(response, "[DONE]", cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not BackendValidationException && ex is not OperationCanceledException)
            {
                lastError = ex;
                if (streamWriter.HasWritten || response.HasStarted)
                {
                    await streamWriter.FlushAsync(cancellationToken);
                    await WriteSseJsonAsync(response, new { error = "The managed provider stream ended unexpectedly." }, cancellationToken);
                    return;
                }
            }
        }

        throw new BackendValidationException(
            lastError == null
                ? "Managed AI request failed."
                : $"Managed AI request failed: {lastError.Message}");
    }

    private void LogTiming(DesktopAiChatRequestDto request, string milestone, Stopwatch stopwatch)
    {
        _logger.LogInformation(
            "Managed AI timing requestId={RequestId} milestone={Milestone} elapsedMs={ElapsedMs} provider={Provider} model={Model} image={HasImage}",
            request.RequestId,
            milestone,
            stopwatch.Elapsed.TotalMilliseconds,
            request.Provider,
            request.Model,
            !string.IsNullOrWhiteSpace(request.ImageBase64));
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
                request.ImageBase64,
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
        var providerCredentials = _credentials.ListByProvider(providerId)
            .Where(item => item.IsEnabled)
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.UpdatedAtUtc)
            .ToArray();

        if (providerCredentials.Length == 0)
        {
            throw new BackendValidationException($"No managed credentials are configured for {providerId}.");
        }

        return providerCredentials;
    }

    private async Task<string> GenerateProviderResponseWithFallbackAsync(
        string providerId,
        string modelId,
        IReadOnlyList<DesktopAiChatMessageDto> messages,
        string? imageBase64,
        CancellationToken cancellationToken)
    {
        var providerCredentials = GetEnabledProviderCredentials(providerId);
        Exception? lastError = null;

        foreach (var credential in providerCredentials)
        {
            try
            {
                var apiKey = _protector.Unprotect(credential.EncryptedApiKey);
                return await GenerateProviderResponseAsync(
                    providerId,
                    modelId,
                    messages,
                    imageBase64,
                    apiKey,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not BackendValidationException)
            {
                lastError = ex;
            }
        }

        throw lastError ?? new InvalidOperationException("Managed AI request failed.");
    }

    private async Task StreamProviderAsync(SseDeltaWriter streamWriter, DesktopAiChatRequestDto request, string apiKey, CancellationToken cancellationToken)
    {
        RunOutputBudgetSelfCheck();
        var outputBudget = GetLiveOutputBudget(request.Messages);
        switch (request.Provider)
        {
            case ManagedAiCatalog.ChatGpt:
                await StreamOpenAiCompatibleAsync(
                    streamWriter,
                    "https://api.openai.com/v1/chat/completions",
                    BuildOpenAiMessages(request.Messages, request.ImageBase64, mistralImageUrl: false),
                    request.Model,
                    outputBudget,
                    apiKey,
                    cancellationToken);
                return;
            case ManagedAiCatalog.Mistral:
                await StreamOpenAiCompatibleAsync(
                    streamWriter,
                    "https://api.mistral.ai/v1/chat/completions",
                    BuildOpenAiMessages(request.Messages, request.ImageBase64, mistralImageUrl: true),
                    request.Model,
                    outputBudget,
                    apiKey,
                    cancellationToken);
                return;
            case ManagedAiCatalog.Groq:
                await StreamOpenAiCompatibleAsync(
                    streamWriter,
                    "https://api.groq.com/openai/v1/chat/completions",
                    BuildOpenAiMessages(request.Messages, request.ImageBase64, mistralImageUrl: false),
                    request.Model,
                    outputBudget,
                    apiKey,
                    cancellationToken);
                return;
            case ManagedAiCatalog.Claude:
                await StreamClaudeAsync(streamWriter, request, apiKey, outputBudget, cancellationToken);
                return;
            case ManagedAiCatalog.Gemini:
                await StreamGeminiAsync(streamWriter, request, apiKey, outputBudget, cancellationToken);
                return;
            case ManagedAiCatalog.Nvidia:
                await StreamOpenAiCompatibleAsync(
                    streamWriter,
                    "https://integrate.api.nvidia.com/v1/chat/completions",
                    BuildOpenAiMessages(request.Messages, request.ImageBase64, mistralImageUrl: false),
                    request.Model,
                    outputBudget,
                    apiKey,
                    cancellationToken);
                return;
            default:
                throw new BackendValidationException("Unsupported managed provider.");
        }
    }

    private async Task<string> GenerateProviderResponseAsync(
        string providerId,
        string modelId,
        IReadOnlyList<DesktopAiChatMessageDto> messages,
        string? imageBase64,
        string apiKey,
        CancellationToken cancellationToken)
    {
        return providerId switch
        {
            ManagedAiCatalog.ChatGpt => await GenerateOpenAiCompatibleResponseAsync(
                "https://api.openai.com/v1/chat/completions",
                BuildOpenAiMessages(messages, imageBase64, mistralImageUrl: false),
                modelId,
                apiKey,
                cancellationToken),
            ManagedAiCatalog.Mistral => await GenerateOpenAiCompatibleResponseAsync(
                "https://api.mistral.ai/v1/chat/completions",
                BuildOpenAiMessages(messages, imageBase64, mistralImageUrl: true),
                modelId,
                apiKey,
                cancellationToken),
            ManagedAiCatalog.Groq => await GenerateOpenAiCompatibleResponseAsync(
                "https://api.groq.com/openai/v1/chat/completions",
                BuildOpenAiMessages(messages, imageBase64, mistralImageUrl: false),
                modelId,
                apiKey,
                cancellationToken),
            ManagedAiCatalog.Claude => await GenerateClaudeResponseAsync(modelId, messages, imageBase64, apiKey, cancellationToken),
            ManagedAiCatalog.Gemini => await GenerateGeminiResponseAsync(modelId, messages, imageBase64, apiKey, cancellationToken),
            ManagedAiCatalog.Nvidia => await GenerateOpenAiCompatibleResponseAsync(
                "https://integrate.api.nvidia.com/v1/chat/completions",
                BuildOpenAiMessages(messages, imageBase64, mistralImageUrl: false),
                modelId,
                apiKey,
                cancellationToken),
            _ => throw new BackendValidationException("Unsupported managed provider.")
        };
    }

    private async Task StreamOpenAiCompatibleAsync(
        SseDeltaWriter streamWriter,
        string url,
        object[] messages,
        string model,
        int outputBudget,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model,
            messages,
            max_tokens = outputBudget,
            stream = true
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        streamWriter.MarkProviderHeaders();
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"{(int)response.StatusCode}: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
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
            if (!json.RootElement.TryGetProperty("choices", out var choices)
                || choices.GetArrayLength() == 0)
            {
                continue;
            }

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta)
                || !delta.TryGetProperty("content", out var contentElement))
            {
                continue;
            }

            var deltaText = contentElement.GetString();
            if (string.IsNullOrEmpty(deltaText))
            {
                continue;
            }

            await streamWriter.AppendAsync(deltaText, cancellationToken);
        }
    }

    private async Task<string> GenerateOpenAiCompatibleResponseAsync(
        string url,
        object[] messages,
        string model,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model,
            messages,
            max_tokens = 64,
            stream = false
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"{(int)response.StatusCode}: {errorBody}");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
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

    private async Task StreamClaudeAsync(SseDeltaWriter streamWriter, DesktopAiChatRequestDto request, string apiKey, int outputBudget, CancellationToken cancellationToken)
    {
        var systemPrompt = request.Messages.FirstOrDefault(item => string.Equals(item.Role, "system", StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty;
        var filteredMessages = request.Messages
            .Where(item => !string.Equals(item.Role, "system", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.Content))
            .ToArray();

        var apiMessages = BuildClaudeMessages(filteredMessages, request.ImageBase64);
        var payload = JsonSerializer.Serialize(new
        {
            model = request.Model,
            max_tokens = outputBudget,
            system = systemPrompt,
            messages = apiMessages,
            stream = true
        });

        using var outbound = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        outbound.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        outbound.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        outbound.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(outbound, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        streamWriter.MarkProviderHeaders();
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"{(int)response.StatusCode}: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
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
            if (!json.RootElement.TryGetProperty("type", out var typeElement)
                || typeElement.GetString() != "content_block_delta")
            {
                continue;
            }

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
    }

    private async Task<string> GenerateClaudeResponseAsync(
        string modelId,
        IReadOnlyList<DesktopAiChatMessageDto> messages,
        string? imageBase64,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var systemPrompt = messages.FirstOrDefault(item => string.Equals(item.Role, "system", StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty;
        var filteredMessages = messages
            .Where(item => !string.Equals(item.Role, "system", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.Content))
            .ToArray();

        var payload = JsonSerializer.Serialize(new
        {
            model = modelId,
            max_tokens = 64,
            system = systemPrompt,
            messages = BuildClaudeMessages(filteredMessages, imageBase64)
        });

        using var outbound = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        outbound.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        outbound.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        outbound.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(outbound, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"{(int)response.StatusCode}: {errorBody}");
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

    private async Task StreamGeminiAsync(SseDeltaWriter streamWriter, DesktopAiChatRequestDto request, string apiKey, int outputBudget, CancellationToken cancellationToken)
    {
        var contents = BuildGeminiContents(request.Messages, request.ImageBase64);
        var payload = JsonSerializer.Serialize(new
        {
            contents,
            generationConfig = new
            {
                maxOutputTokens = outputBudget,
                temperature = 0.7
            }
        });

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(request.Model)}:streamGenerateContent?key={Uri.EscapeDataString(apiKey)}&alt=sse";
        using var outbound = new HttpRequestMessage(HttpMethod.Post, url);
        outbound.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(outbound, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        streamWriter.MarkProviderHeaders();
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"{(int)response.StatusCode}: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
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
            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.GetArrayLength() == 0)
            {
                continue;
            }

            if (!parts[0].TryGetProperty("text", out var textElement))
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

    private async Task<string> GenerateGeminiResponseAsync(
        string modelId,
        IReadOnlyList<DesktopAiChatMessageDto> messages,
        string? imageBase64,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            contents = BuildGeminiContents(messages, imageBase64),
            generationConfig = new
            {
                maxOutputTokens = 64,
                temperature = 0.2
            }
        });

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(modelId)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
        using var outbound = new HttpRequestMessage(HttpMethod.Post, url);
        outbound.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(outbound, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"{(int)response.StatusCode}: {errorBody}");
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

    private static object[] BuildOpenAiMessages(IReadOnlyList<DesktopAiChatMessageDto> messages, string? imageBase64, bool mistralImageUrl)
    {
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
                var attachImage = index == lastUserIndex
                    && string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(imageBase64);

                if (!attachImage)
                {
                    return (object)new
                    {
                        role = item.Role,
                        content = item.Content
                    };
                }

                return new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = item.Content },
                        mistralImageUrl
                            ? (object)new { type = "image_url", image_url = $"data:image/png;base64,{imageBase64}" }
                            : (object)new { type = "image_url", image_url = new { url = $"data:image/png;base64,{imageBase64}" } }
                    }
                };
            })
            .ToArray();
    }

    private static object[] BuildClaudeMessages(IReadOnlyList<DesktopAiChatMessageDto> filteredMessages, string? imageBase64)
    {
        return filteredMessages
            .Select((item, index) =>
            {
                var isLastUser = string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase)
                    && index == filteredMessages.Count - 1;

                if (isLastUser && !string.IsNullOrWhiteSpace(imageBase64))
                {
                    return (object)new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "image",
                                source = new
                                {
                                    type = "base64",
                                    media_type = "image/png",
                                    data = imageBase64
                                }
                            },
                            new { type = "text", text = item.Content }
                        }
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

    private static object[] BuildGeminiContents(IReadOnlyList<DesktopAiChatMessageDto> messages, string? imageBase64)
    {
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

            var attachImage = i == filtered.Length - 1
                && role == "user"
                && !string.IsNullOrWhiteSpace(imageBase64);

            if (attachImage)
            {
                items.Add(new
                {
                    role,
                    parts = new object[]
                    {
                        new { text },
                        new
                        {
                            inline_data = new
                            {
                                mime_type = "image/png",
                                data = imageBase64
                            }
                        }
                    }
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

    private static int GetLiveOutputBudget(IReadOnlyList<DesktopAiChatMessageDto> messages)
    {
        var question = messages.LastOrDefault(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content
            ?.ToLowerInvariant() ?? string.Empty;
        if (ContainsAny(question, "expand", "deeper", "in detail", "step by step")) return 1000;
        if (ContainsAny(question, "write code", "implement", "algorithm", "complexity", "debug this")) return 450;
        if (ContainsAny(question, "system design", "design a", "architecture", "scalability", "high availability")) return 550;
        if (ContainsAny(question, "my project", "your project", "project called", "project named")) return 320;
        if (question.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 12
            && ContainsAny(question, "why", "how", "what about", "give an example", "clarify")) return 160;
        return 250;
    }

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(term => value.Contains(term, StringComparison.Ordinal));

    [Conditional("DEBUG")]
    private static void RunOutputBudgetSelfCheck()
    {
        static DesktopAiChatMessageDto User(string content) => new() { Role = "user", Content = content };
        Debug.Assert(GetLiveOutputBudget(new[] { User("Why?") }) == 160);
        Debug.Assert(GetLiveOutputBudget(new[] { User("Design a highly available payment system") }) == 550);
        Debug.Assert(GetLiveOutputBudget(new[] { User("Expand in detail") }) == 1000);
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

        public SseDeltaWriter(HttpResponse response, Action<string> mark)
        {
            _response = response;
            _mark = mark;
        }

        public bool HasWritten { get; private set; }

        public void MarkProviderHeaders()
        {
            if (_providerHeadersMarked) return;
            _providerHeadersMarked = true;
            _mark("provider_headers_received");
        }

        public async Task AppendAsync(string delta, CancellationToken cancellationToken)
        {
            if (!_firstTokenMarked)
            {
                _firstTokenMarked = true;
                _mark("first_upstream_token");
                await WriteSseJsonAsync(_response, new { delta }, cancellationToken);
                HasWritten = true;
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
            _sinceFlush.Restart();
        }
    }
}
