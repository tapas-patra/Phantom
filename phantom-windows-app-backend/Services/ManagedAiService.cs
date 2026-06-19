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

    public ManagedAiService(
        ManagedProviderCredentialRepository credentials,
        SecretProtector protector,
        AuthSessionRepository sessions,
        AccountRepository accounts,
        TokenService tokens,
        ManagedAiCatalogService catalogService)
    {
        _credentials = credentials;
        _protector = protector;
        _sessions = sessions;
        _accounts = accounts;
        _tokens = tokens;
        _catalogService = catalogService;
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

        var providerCredentials = _credentials.ListByProvider(request.Provider)
            .Where(item => item.IsEnabled)
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.UpdatedAtUtc)
            .ToArray();

        if (providerCredentials.Length == 0)
        {
            throw new BackendValidationException($"No managed credentials are configured for {request.Provider}.");
        }

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";

        Exception? lastError = null;
        foreach (var credential in providerCredentials)
        {
            try
            {
                var apiKey = _protector.Unprotect(credential.EncryptedApiKey);
                await StreamProviderAsync(response, request, apiKey, cancellationToken);
                await WriteSseDataAsync(response, "[DONE]", cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not BackendValidationException && ex is not OperationCanceledException)
            {
                lastError = ex;
            }
        }

        throw new BackendValidationException(
            lastError == null
                ? "Managed AI request failed."
                : $"Managed AI request failed: {lastError.Message}");
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

    private async Task StreamProviderAsync(HttpResponse downstreamResponse, DesktopAiChatRequestDto request, string apiKey, CancellationToken cancellationToken)
    {
        switch (request.Provider)
        {
            case ManagedAiCatalog.ChatGpt:
                await StreamOpenAiCompatibleAsync(
                    downstreamResponse,
                    "https://api.openai.com/v1/chat/completions",
                    BuildOpenAiMessages(request.Messages, request.ImageBase64, mistralImageUrl: false),
                    request.Model,
                    apiKey,
                    cancellationToken);
                return;
            case ManagedAiCatalog.Mistral:
                await StreamOpenAiCompatibleAsync(
                    downstreamResponse,
                    "https://api.mistral.ai/v1/chat/completions",
                    BuildOpenAiMessages(request.Messages, request.ImageBase64, mistralImageUrl: true),
                    request.Model,
                    apiKey,
                    cancellationToken);
                return;
            case ManagedAiCatalog.Groq:
                await StreamOpenAiCompatibleAsync(
                    downstreamResponse,
                    "https://api.groq.com/openai/v1/chat/completions",
                    BuildOpenAiMessages(request.Messages, request.ImageBase64, mistralImageUrl: false),
                    request.Model,
                    apiKey,
                    cancellationToken);
                return;
            case ManagedAiCatalog.Claude:
                await StreamClaudeAsync(downstreamResponse, request, apiKey, cancellationToken);
                return;
            case ManagedAiCatalog.Gemini:
                await StreamGeminiAsync(downstreamResponse, request, apiKey, cancellationToken);
                return;
            case ManagedAiCatalog.Nvidia:
                await StreamOpenAiCompatibleAsync(
                    downstreamResponse,
                    "https://integrate.api.nvidia.com/v1/chat/completions",
                    BuildOpenAiMessages(request.Messages, request.ImageBase64, mistralImageUrl: false),
                    request.Model,
                    apiKey,
                    cancellationToken);
                return;
            default:
                throw new BackendValidationException("Unsupported managed provider.");
        }
    }

    private async Task StreamOpenAiCompatibleAsync(
        HttpResponse downstreamResponse,
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
            max_tokens = 2000,
            stream = true
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

            await WriteSseJsonAsync(downstreamResponse, new { delta = deltaText }, cancellationToken);
        }
    }

    private async Task StreamClaudeAsync(HttpResponse downstreamResponse, DesktopAiChatRequestDto request, string apiKey, CancellationToken cancellationToken)
    {
        var systemPrompt = request.Messages.FirstOrDefault(item => string.Equals(item.Role, "system", StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty;
        var filteredMessages = request.Messages
            .Where(item => !string.Equals(item.Role, "system", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.Content))
            .ToArray();

        var apiMessages = filteredMessages
            .Select((item, index) =>
            {
                var isLastUser = string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase)
                    && index == filteredMessages.Length - 1;

                if (isLastUser && !string.IsNullOrWhiteSpace(request.ImageBase64))
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
                                    data = request.ImageBase64
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

        var payload = JsonSerializer.Serialize(new
        {
            model = request.Model,
            max_tokens = 2000,
            system = systemPrompt,
            messages = apiMessages,
            stream = true
        });

        using var outbound = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        outbound.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        outbound.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        outbound.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(outbound, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

            await WriteSseJsonAsync(downstreamResponse, new { delta = deltaText }, cancellationToken);
        }
    }

    private async Task StreamGeminiAsync(HttpResponse downstreamResponse, DesktopAiChatRequestDto request, string apiKey, CancellationToken cancellationToken)
    {
        var contents = BuildGeminiContents(request.Messages, request.ImageBase64);
        var payload = JsonSerializer.Serialize(new
        {
            contents,
            generationConfig = new
            {
                maxOutputTokens = 2000,
                temperature = 0.7
            }
        });

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(request.Model)}:streamGenerateContent?key={Uri.EscapeDataString(apiKey)}&alt=sse";
        using var outbound = new HttpRequestMessage(HttpMethod.Post, url);
        outbound.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(outbound, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

            await WriteSseJsonAsync(downstreamResponse, new { delta = deltaText }, cancellationToken);
        }
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

    private static async Task WriteSseJsonAsync(HttpResponse response, object payload, CancellationToken cancellationToken)
    {
        await WriteSseDataAsync(response, JsonSerializer.Serialize(payload), cancellationToken);
    }

    private static async Task WriteSseDataAsync(HttpResponse response, string data, CancellationToken cancellationToken)
    {
        await response.WriteAsync($"data: {data}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
