using System;
using System.Collections.Generic;
using System.IO;
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

        public HostedManagedAiService(
            IAuthSessionRepository authSessions,
            HostedRuntimeOptions options,
            string provider,
            string model)
        {
            _authSessions = authSessions;
            _options = options;
            _provider = provider;
            _model = model;
        }

        public string GetProviderName() => _provider;

        public bool IsConfigured()
        {
            var session = _authSessions.Load();
            return session != null
                && session.IsAuthenticated
                && !string.IsNullOrWhiteSpace(session.AccessToken)
                && !string.IsNullOrWhiteSpace(_options.DesktopBackendBaseUrl);
        }

        public async Task<string> SendMessageAsync(List<ConversationMessage> messages, string? imageBase64 = null)
        {
            var builder = new StringBuilder();
            return await SendMessageStreamAsync(messages, chunk => builder.Append(chunk), CancellationToken.None, imageBase64);
        }

        public async Task<string> SendMessageStreamAsync(
            List<ConversationMessage> messages,
            Action<string> onChunkReceived,
            CancellationToken cancellationToken = default,
            string? imageBase64 = null)
        {
            var session = _authSessions.Load();
            if (session == null || !session.IsAuthenticated || string.IsNullOrWhiteSpace(session.AccessToken))
            {
                return "Error: Hosted desktop session not found. Please sign in again.";
            }

            try
            {
                var payload = new
                {
                    provider = _provider,
                    model = _model,
                    imageBase64,
                    messages = messages.ConvertAll(message => new
                    {
                        role = message.Role,
                        content = message.Content
                    })
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.DesktopBackendBaseUrl}/api/desktop/ai/chat");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
                request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                using var response = await HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    return $"Error: {(int)response.StatusCode} - {errorBody}";
                }

                var fullResponse = new StringBuilder();
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
                            break;
                        }

                        try
                        {
                            var chunk = JObject.Parse(data);
                            var delta = chunk["delta"]?.Value<string>();
                            if (!string.IsNullOrWhiteSpace(delta))
                            {
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

                return fullResponse.ToString();
            }
            catch (OperationCanceledException)
            {
                return "Error: Request cancelled";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}
