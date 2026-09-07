using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Services
{
    public sealed class SpeechTranscriptionClient
    {
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        private readonly AppSettings _settings;
        private readonly string _backendBaseUrl;
        private readonly string _accessToken;
        private readonly bool _managed;

        public SpeechTranscriptionClient(AppSettings settings, string backendBaseUrl, string accessToken, bool managed)
        {
            _settings = settings;
            _backendBaseUrl = backendBaseUrl.TrimEnd('/');
            _accessToken = accessToken;
            _managed = managed;
        }

        public async Task<string> TranscribePcm16Async(byte[] pcm16, CancellationToken cancellationToken)
        {
            if (!ContainsSpeech(pcm16))
            {
                Log.WriteLine("Cloud speech skipped reason=no_speech_detected");
                return string.Empty;
            }
            var wav = BuildWav(pcm16, 16_000, 1);
            if (_managed) return await TranscribeManagedAsync(wav, cancellationToken);

            var keys = GetByoKeys();
            if (keys.Count == 0) throw new InvalidOperationException("No speech API key is configured.");
            Exception? lastError = null;
            for (var attempt = 0; attempt < Math.Min(keys.Count, ProviderResiliencePolicy.ByoDesktopMaxAttempts); attempt++)
            {
                var selected = NextAvailableKey(keys);
                if (selected == null) break;
                try
                {
                    var text = await PostTranscriptionAsync(GetProviderUrl(_settings.SpeechProviderId), selected.Value.Key, wav, _settings.SpeechModelId, _settings.SpeechLanguage, cancellationToken);
                    _settings.SpeechRotationState.KeyCooldownUntilUtc.Remove(CooldownName(selected.Value.Index));
                    SettingsManager.Save(_settings);
                    Log.WriteLine($"BYO speech provider completed provider={_settings.SpeechProviderId} model={_settings.SpeechModelId} key_slot={selected.Value.Index + 1} attempt={attempt + 1}");
                    return text;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = ex;
                    var failure = ProviderResiliencePolicy.Classify(ex.Message);
                    Log.WriteLine($"BYO speech provider failed provider={_settings.SpeechProviderId} model={_settings.SpeechModelId} attempt={attempt + 1} error_code={failure.ErrorCode}");
                    if (failure.CanRotateCredential)
                    {
                        _settings.SpeechRotationState.KeyCooldownUntilUtc[CooldownName(selected.Value.Index)] = DateTime.UtcNow.Add(failure.Cooldown);
                        SettingsManager.Save(_settings);
                    }
                    if (!ProviderResiliencePolicy.CanRetry(failure, attempt + 1, ProviderResiliencePolicy.ByoDesktopMaxAttempts, false)) break;
                }
            }
            throw lastError ?? new InvalidOperationException("No speech API key is currently available.");
        }

        private async Task<string> TranscribeManagedAsync(byte[] wav, CancellationToken cancellationToken)
        {
            using var form = BuildForm(wav, model: null, _settings.SpeechLanguage);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_backendBaseUrl}/api/desktop/speech/transcribe") { Content = form };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Managed speech failed ({(int)response.StatusCode}).");
            var result = JsonConvert.DeserializeObject<SpeechTranscriptionResponseDto>(body);
            Log.WriteLine($"Managed speech provider completed provider={result?.ProviderId ?? "unknown"} model={result?.ModelId ?? "unknown"}");
            return result?.Text?.Trim() ?? string.Empty;
        }

        private static async Task<string> PostTranscriptionAsync(string url, string apiKey, byte[] wav, string model, string language, CancellationToken cancellationToken)
        {
            using var form = BuildForm(wav, model, language);
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Speech provider failed ({(int)response.StatusCode}).");
            using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(body));
            var result = Newtonsoft.Json.Linq.JObject.Load(reader);
            return result.Value<string>("text")?.Trim() ?? string.Empty;
        }

        private static MultipartFormDataContent BuildForm(byte[] wav, string? model, string language)
        {
            var form = new MultipartFormDataContent();
            var audio = new ByteArrayContent(wav);
            audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(audio, string.IsNullOrWhiteSpace(model) ? "audio" : "file", "speech.wav");
            if (!string.IsNullOrWhiteSpace(model))
            {
                form.Add(new StringContent(model), "model");
                form.Add(new StringContent("json"), "response_format");
            }
            if (!string.IsNullOrWhiteSpace(language)) form.Add(new StringContent(language), "language");
            return form;
        }

        private List<string> GetByoKeys()
        {
            if (!_settings.UseChatProviderApiKeysForSpeech)
                return _settings.SpeechApiKeys.TryGetValue(_settings.SpeechProviderId, out var dedicated) ? dedicated : new List<string>();
            return _settings.SpeechProviderId switch
            {
                "ChatGPT" => _settings.ChatGPTApiKeys,
                "Groq" => _settings.GroqApiKeys,
                "Mistral" => _settings.MistralApiKeys,
                _ => new List<string>()
            };
        }

        private (int Index, string Key)? NextAvailableKey(IReadOnlyList<string> keys)
        {
            var state = _settings.SpeechRotationState;
            var scope = RotationScope;
            var last = state.LastKeyIndex.TryGetValue(scope, out var value) ? value : -1;
            for (var offset = 1; offset <= keys.Count; offset++)
            {
                var index = (last + offset) % keys.Count;
                if (state.KeyCooldownUntilUtc.TryGetValue(CooldownName(index), out var until) && until > DateTime.UtcNow) continue;
                state.LastKeyIndex[scope] = index;
                return (index, keys[index]);
            }
            return null;
        }

        private string RotationScope => $"{_settings.SpeechProviderId}:{(_settings.UseChatProviderApiKeysForSpeech ? "chat" : "dedicated")}";
        private string CooldownName(int index) => $"{RotationScope}:{index}";

        private static string GetProviderUrl(string provider) => provider switch
        {
            "ChatGPT" => "https://api.openai.com/v1/audio/transcriptions",
            "Groq" => "https://api.groq.com/openai/v1/audio/transcriptions",
            "Mistral" => "https://api.mistral.ai/v1/audio/transcriptions",
            _ => throw new InvalidOperationException("Unsupported speech provider.")
        };

        private static byte[] BuildWav(byte[] pcm, int sampleRate, short channels)
        {
            using var output = new MemoryStream(44 + pcm.Length);
            using var writer = new BinaryWriter(output, Encoding.ASCII, true);
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + pcm.Length); writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt ")); writer.Write(16); writer.Write((short)1); writer.Write(channels);
            writer.Write(sampleRate); writer.Write(sampleRate * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length); writer.Write(pcm);
            return output.ToArray();
        }

        private static bool ContainsSpeech(byte[] pcm)
        {
            if (pcm.Length < 2) return false;
            long total = 0;
            var count = 0;
            for (var index = 0; index + 1 < pcm.Length; index += 16)
            {
                total += Math.Abs((int)BitConverter.ToInt16(pcm, index));
                count++;
            }
            return count > 0 && total / count >= 120;
        }
    }
}
