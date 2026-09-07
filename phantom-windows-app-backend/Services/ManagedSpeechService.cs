using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class ManagedSpeechService
{
    private const long MaxAudioBytes = 5 * 1024 * 1024;
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly ManagedProviderCredentialRepository _credentials;
    private readonly ManagedSpeechCatalogService _catalog;
    private readonly SecretProtector _protector;
    private readonly ILogger<ManagedSpeechService> _logger;

    public ManagedSpeechService(
        ManagedProviderCredentialRepository credentials,
        ManagedSpeechCatalogService catalog,
        SecretProtector protector,
        ILogger<ManagedSpeechService> logger)
    {
        _credentials = credentials;
        _catalog = catalog;
        _protector = protector;
        _logger = logger;
    }

    public IReadOnlyList<ManagedAiProviderKeyDto> ListAdminCredentials() =>
        _credentials.ListAll(ManagedSpeechCatalogService.Workload).Select(MapCredential).ToArray();

    public ManagedAiProviderKeyDto UpsertCredential(ManagedAiProviderKeyUpsertRequestDto request)
    {
        if (!ManagedSpeechCatalog.IsAllowedProvider(request.ProviderId))
            throw new BackendValidationException("Unsupported speech provider.");
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            throw new BackendValidationException("API key is required.");

        var now = DateTime.UtcNow;
        var existing = string.IsNullOrWhiteSpace(request.CredentialId) ? null : _credentials.FindById(request.CredentialId);
        if (existing != null && !string.Equals(existing.Workload, ManagedSpeechCatalogService.Workload, StringComparison.Ordinal))
            throw new BackendValidationException("Credential belongs to a different workload.");

        var record = existing ?? new ManagedProviderCredentialRecord
        {
            CredentialId = $"speech-key-{Guid.NewGuid():N}",
            Workload = ManagedSpeechCatalogService.Workload,
            CreatedAtUtc = now
        };
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
        return MapCredential(record);
    }

    public void DeleteCredential(string credentialId)
    {
        var record = _credentials.FindById(credentialId)
            ?? throw new BackendValidationException("Speech credential not found.");
        if (!string.Equals(record.Workload, ManagedSpeechCatalogService.Workload, StringComparison.Ordinal))
            throw new BackendValidationException("Credential belongs to a different workload.");
        _credentials.Delete(record.CredentialId);
    }

    public async Task<SpeechTranscriptionResponseDto> TranscribeAsync(
        DesktopAccountRecord account,
        IFormFile audio,
        string? language,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(AccessModeResolver.GetEffectiveAccessTier(account), AccessModeResolver.Premium, StringComparison.OrdinalIgnoreCase))
            throw new BackendValidationException("Managed speech recognition is available only for Premium accounts.");
        if (audio.Length < 512 || audio.Length > MaxAudioBytes)
            throw new BackendValidationException("Audio chunk must be between 512 bytes and 5 MB.");
        await using (var validationStream = audio.OpenReadStream())
        {
            var header = new byte[12];
            await validationStream.ReadExactlyAsync(header, cancellationToken);
            if (Encoding.ASCII.GetString(header, 0, 4) != "RIFF" || Encoding.ASCII.GetString(header, 8, 4) != "WAVE")
                throw new BackendValidationException("Only PCM WAV speech chunks are accepted.");
        }
        language = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim();
        if (language.Length > 12 || language.Any(character => !char.IsAsciiLetter(character) && character != '-'))
            throw new BackendValidationException("Language must be an ISO language code.");

        var selection = _catalog.RequireResolvedSelection();
        var now = DateTime.UtcNow;
        var candidates = _credentials.ListByProvider(selection.ProviderId, ManagedSpeechCatalogService.Workload)
            .Where(item => item.IsEnabled && (!item.CooldownUntilUtc.HasValue || item.CooldownUntilUtc <= now))
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.UpdatedAtUtc)
            .Take(ProviderResiliencePolicy.ManagedBackendMaxAttempts)
            .ToArray();
        if (candidates.Length == 0)
            throw new ManagedAiProviderException("speech_credentials_unavailable", true);

        Exception? lastError = null;
        foreach (var credential in candidates)
        {
            try
            {
                await using var source = audio.OpenReadStream();
                var text = await TranscribeProviderAsync(
                    selection.ProviderId,
                    selection.ModelId,
                    _protector.Unprotect(credential.EncryptedApiKey),
                    source,
                    language,
                    cancellationToken);
                _credentials.RecordSuccess(credential.CredentialId);
                return new SpeechTranscriptionResponseDto { Text = text, ProviderId = selection.ProviderId, ModelId = selection.ModelId };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastError = ex;
                var failure = ProviderResiliencePolicy.Classify(ex);
                if (failure.CanRotateCredential && failure.Cooldown > TimeSpan.Zero)
                    _credentials.RecordFailure(credential.CredentialId, failure.ErrorCode, DateTime.UtcNow.Add(failure.Cooldown));
                _logger.LogWarning("Managed speech attempt failed provider={Provider} model={Model} error={Error}", selection.ProviderId, selection.ModelId, failure.ErrorCode);
                if (!failure.CanRotateCredential) break;
            }
        }

        throw ManagedAiProviderException.FromFailure(lastError);
    }

    private static async Task<string> TranscribeProviderAsync(
        string provider,
        string model,
        string apiKey,
        Stream audio,
        string? language,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var audioContent = new StreamContent(audio);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audioContent, "file", "speech.wav");
        form.Add(new StringContent(model), "model");
        form.Add(new StringContent("json"), "response_format");
        if (!string.IsNullOrWhiteSpace(language)) form.Add(new StringContent(language.Trim()), "language");

        using var request = new HttpRequestMessage(HttpMethod.Post, ManagedSpeechCatalog.GetTranscriptionUrl(provider));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = form;
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw ManagedAiProviderException.FromStatusCode((int)response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("text", out var text) ? text.GetString()?.Trim() ?? string.Empty : string.Empty;
    }

    private static ManagedAiProviderKeyDto MapCredential(ManagedProviderCredentialRecord record) => new()
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
