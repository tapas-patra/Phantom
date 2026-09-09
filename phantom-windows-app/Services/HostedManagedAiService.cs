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
using Newtonsoft.Json.Linq;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Infrastructure.Hosted;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Services
{
    public sealed class HostedManagedAiService : IAIService
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        private readonly IAuthSessionRepository _authSessions;
        private readonly HostedRuntimeOptions _options;
        private readonly string _provider;
        private readonly string _model;
        private readonly bool _allowPaidSessionExtension;

        public HostedManagedAiService(
            IAuthSessionRepository authSessions,
            HostedRuntimeOptions options,
            string provider,
            string model,
            bool allowPaidSessionExtension)
        {
            _authSessions = authSessions;
            _options = options;
            _provider = provider;
            _model = model;
            _allowPaidSessionExtension = allowPaidSessionExtension;
        }

        public string GetProviderName() => _provider;
        public string GetModelName() => _model;

        public bool IsConfigured()
        {
            var session = _authSessions.Load();
            return session != null
                && session.IsAuthenticated
                && !string.IsNullOrWhiteSpace(session.AccessToken)
                && !string.IsNullOrWhiteSpace(_options.DesktopBackendBaseUrl);
        }

        public bool HasUsableSession()
        {
            return EnsureValidSession() != null;
        }

        public async Task<string> SendMessageAsync(List<ConversationMessage> messages, IReadOnlyList<string>? imagesBase64 = null)
        {
            var builder = new StringBuilder();
            return await SendMessageStreamAsync(messages, chunk => builder.Append(chunk), CancellationToken.None, imagesBase64);
        }

        public async Task<string> SendMessageStreamAsync(
            List<ConversationMessage> messages,
            Action<string> onChunkReceived,
            CancellationToken cancellationToken = default,
            IReadOnlyList<string>? imagesBase64 = null)
        {
            var session = EnsureValidSession();
            if (session == null || !session.IsAuthenticated || string.IsNullOrWhiteSpace(session.AccessToken))
            {
                return "Error: Hosted desktop session not found. Please sign in again.";
            }

            try
            {
                var payload = new
                {
                    requestId = LiveRequestTrace.Current?.OperationId ?? Guid.NewGuid().ToString("N"),
                    turnId = LiveRequestTrace.Current?.TurnId ?? string.Empty,
                    provider = _provider,
                    model = _model,
                    allowPaidSessionExtension = _allowPaidSessionExtension,
                    imageBase64 = MultimodalContentBuilder.Normalize(imagesBase64).FirstOrDefault(),
                    imagesBase64 = MultimodalContentBuilder.Normalize(imagesBase64).Take(3).ToList(),
                    messages = messages.ConvertAll(message => new
                    {
                        role = message.Role,
                        content = message.Content
                    })
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.DesktopBackendBaseUrl}/api/desktop/ai/chat");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
                AddCorrelationHeaders(request);
                request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                using var response = await HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                LiveRequestTrace.Current?.Mark("provider_headers_received");

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    if (ShouldRetryWithRefresh(response.StatusCode, errorBody))
                    {
                        var refreshedSession = TryRefreshSession(_authSessions.Load());
                        if (refreshedSession != null && !string.IsNullOrWhiteSpace(refreshedSession.AccessToken))
                        {
                            return await RetryWithSessionAsync(
                                refreshedSession,
                                messages,
                                onChunkReceived,
                                cancellationToken,
                                imagesBase64);
                        }
                    }

                    return $"Error: provider_http_{(int)response.StatusCode}";
                }

                var fullResponse = new StringBuilder();
                var sawDone = false;
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream))
                {
                    while (!reader.EndOfStream)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var line = await reader.ReadLineAsync();
                        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                        {
                            continue;
                        }

                        var data = line.Substring(6);
                        if (data == "[DONE]")
                        {
                            sawDone = true;
                            break;
                        }

                        try
                        {
                            var chunk = JObject.Parse(data);
                            var streamError = chunk["error"]?.Value<string>();
                            if (!string.IsNullOrWhiteSpace(streamError))
                            {
                                return "Error: provider_stream_error";
                            }
                            var delta = chunk["delta"]?.Value<string>();
                            if (!string.IsNullOrWhiteSpace(delta))
                            {
                                LiveRequestTrace.Current?.Mark("first_upstream_token");
                                fullResponse.Append(delta);
                                onChunkReceived?.Invoke(delta);
                            }
                        }
                        catch
                        {
                            // Ignore malformed downstream chunk payloads.
                        }
                    }
                }
                if (!sawDone) return "Error: provider_stream_incomplete";
                return fullResponse.ToString();
            }
            catch (OperationCanceledException)
            {
                return "Error: Request cancelled";
            }
            catch (Exception)
            {
                return "Error: provider_transport_error";
            }
        }

        private AuthSessionCache? EnsureValidSession()
        {
            var session = _authSessions.Load();
            if (session == null || !session.IsAuthenticated)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(session.AccessToken))
            {
                return TryRefreshSession(session);
            }

            if (session.ExpiresAtUtc.HasValue && session.ExpiresAtUtc.Value <= DateTime.UtcNow.AddMinutes(1))
            {
                return TryRefreshSession(session) ?? session;
            }

            return session;
        }

        private async Task<string> RetryWithSessionAsync(
            AuthSessionCache session,
            List<ConversationMessage> messages,
            Action<string> onChunkReceived,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? imagesBase64)
        {
            var payload = new
            {
                requestId = LiveRequestTrace.Current?.OperationId ?? Guid.NewGuid().ToString("N"),
                turnId = LiveRequestTrace.Current?.TurnId ?? string.Empty,
                provider = _provider,
                model = _model,
                allowPaidSessionExtension = _allowPaidSessionExtension,
                imageBase64 = MultimodalContentBuilder.Normalize(imagesBase64).FirstOrDefault(),
                imagesBase64 = MultimodalContentBuilder.Normalize(imagesBase64).Take(3).ToList(),
                messages = messages.ConvertAll(message => new
                {
                    role = message.Role,
                    content = message.Content
                })
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.DesktopBackendBaseUrl}/api/desktop/ai/chat");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            AddCorrelationHeaders(request);
            request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            LiveRequestTrace.Current?.Mark("provider_headers_received");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                return $"Error: provider_http_{(int)response.StatusCode}";
            }

            var fullResponse = new StringBuilder();
            var sawDone = false;
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream))
            {
                while (!reader.EndOfStream)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                    {
                        continue;
                    }

                    var data = line.Substring(6);
                    if (data == "[DONE]")
                    {
                        sawDone = true;
                        break;
                    }

                    try
                    {
                        var chunk = JObject.Parse(data);
                        var streamError = chunk["error"]?.Value<string>();
                        if (!string.IsNullOrWhiteSpace(streamError))
                        {
                            return "Error: provider_stream_error";
                        }
                        var delta = chunk["delta"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(delta))
                    {
                        LiveRequestTrace.Current?.Mark("first_upstream_token");
                            fullResponse.Append(delta);
                            onChunkReceived?.Invoke(delta);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            if (!sawDone) return "Error: provider_stream_incomplete";
            return fullResponse.ToString();
        }

        private AuthSessionCache? TryRefreshSession(AuthSessionCache? session)
        {
            if (session == null
                || string.IsNullOrWhiteSpace(session.RefreshToken)
                || string.IsNullOrWhiteSpace(_options.DesktopBackendBaseUrl))
            {
                return null;
            }

            try
            {
                var request = new AuthRefreshRequestDto
                {
                    RefreshToken = session.RefreshToken,
                    InstallId = session.DeviceInstallId,
                    DeviceFingerprintHash = session.DeviceFingerprintHash
                };

                using var response = HttpClient.PostAsync(
                    $"{_options.DesktopBackendBaseUrl}/api/desktop/auth/refresh",
                    new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    _authSessions.Clear();
                    return null;
                }

                var refreshed = JsonConvert.DeserializeObject<AuthSessionDto>(body);
                if (refreshed == null)
                {
                    return null;
                }

                var updated = MapAuthSession(refreshed);
                _authSessions.Save(updated);
                return updated;
            }
            catch
            {
                _authSessions.Clear();
                return null;
            }
        }

        private static bool ShouldRetryWithRefresh(System.Net.HttpStatusCode statusCode, string errorBody)
        {
            if ((int)statusCode == 401)
            {
                return true;
            }

            if ((int)statusCode != 400)
            {
                return false;
            }

            return errorBody.IndexOf("Desktop session is no longer valid", StringComparison.OrdinalIgnoreCase) >= 0
                || errorBody.IndexOf("Desktop session not found", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddCorrelationHeaders(HttpRequestMessage request)
        {
            var trace = LiveRequestTrace.Current;
            if (trace == null) return;
            request.Headers.TryAddWithoutValidation("X-Phantom-Correlation-Id", trace.TurnId);
            request.Headers.TryAddWithoutValidation("X-Phantom-Operation-Id", trace.OperationId);
        }

        private static AuthSessionCache MapAuthSession(AuthSessionDto sessionDto)
        {
            return new AuthSessionCache
            {
                UserId = sessionDto.UserId,
                Email = sessionDto.Email,
                AccessToken = sessionDto.AccessToken,
                RefreshToken = sessionDto.RefreshToken,
                DeviceInstallId = sessionDto.DeviceInstallId,
                DeviceFingerprintHash = sessionDto.DeviceFingerprintHash,
                AuthMethod = sessionDto.AuthMethod,
                AuthenticatedAtUtc = sessionDto.AuthenticatedAtUtc,
                ExpiresAtUtc = sessionDto.ExpiresAtUtc,
                IsAuthenticated = sessionDto.IsAuthenticated
            };
        }
    }
}
