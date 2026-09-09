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

namespace SecureOverlay.Services
{
    public class NvidiaService : IAIService
    {
        private readonly string _apiKey;
        private readonly string _model;
        private static readonly HttpClient _httpClient = new HttpClient();

        public NvidiaService(string apiKey, string model)
        {
            _apiKey = apiKey;
            _model = model;
        }

        public string GetProviderName() => "NVIDIA";

        public bool IsConfigured() => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_model);

        public async Task<string> SendMessageAsync(List<ConversationMessage> messages, IReadOnlyList<string>? imagesBase64 = null)
        {
            if (!IsConfigured())
                return "Error: NVIDIA API key or model not configured. Go to Settings.";

            var (shouldError, errorMsg) = ErrorSimulator.ShouldSimulateError();
            if (shouldError)
            {
                await Task.Delay(500);
                return errorMsg;
            }

            try
            {
                var apiMessages = BuildMessages(messages, imagesBase64);
                var request = new
                {
                    model = _model,
                    messages = apiMessages,
                    max_tokens = 2000
                };

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://integrate.api.nvidia.com/v1/chat/completions")
                {
                    Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json")
                };
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                var response = await _httpClient.SendAsync(requestMessage);
                var responseJson = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    return $"Error: {response.StatusCode} - {responseJson}";
                }

                dynamic? result = JsonConvert.DeserializeObject(responseJson);
                return result?.choices[0]?.message?.content?.ToString() ?? "No response";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        public async Task<string> SendMessageStreamAsync(
            List<ConversationMessage> messages,
            Action<string> onChunkReceived,
            CancellationToken cancellationToken = default,
            IReadOnlyList<string>? imagesBase64 = null)
        {
            if (!IsConfigured())
                return "Error: NVIDIA API key or model not configured. Go to Settings.";

            var (shouldError, errorMsg) = ErrorSimulator.ShouldSimulateError();
            if (shouldError)
            {
                await Task.Delay(500, cancellationToken);
                return errorMsg;
            }

            try
            {
                var request = new
                {
                    model = _model,
                    messages = BuildMessages(messages, imagesBase64),
                    max_tokens = 2000,
                    stream = true
                };

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://integrate.api.nvidia.com/v1/chat/completions")
                {
                    Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json")
                };
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                var response = await _httpClient.SendAsync(
                    requestMessage,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorJson = await response.Content.ReadAsStringAsync();
                    return $"Error: {response.StatusCode} - {errorJson}";
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
                            dynamic? chunk = JsonConvert.DeserializeObject(data);
                            var delta = chunk?.choices[0]?.delta?.content?.ToString();
                            if (!string.IsNullOrEmpty(delta))
                            {
                                fullResponse.Append(delta);
                                onChunkReceived(delta);
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
            catch (OperationCanceledException)
            {
                return "Cancelled";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private static List<object> BuildMessages(List<ConversationMessage> messages, IReadOnlyList<string>? imagesBase64)
        {
            var apiMessages = new List<object>();
            foreach (var msg in messages.Where(m => !string.IsNullOrWhiteSpace(m.Content)))
            {
                bool isLastUserMessage = msg == messages.Last(m => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content));
                if (msg.Role == "user" && isLastUserMessage && MultimodalContentBuilder.HasImages(imagesBase64))
                {
                    apiMessages.Add(new
                    {
                        role = "user",
                        content = MultimodalContentBuilder.BuildOpenAiContent(
                            msg.Content,
                            MultimodalContentBuilder.Normalize(imagesBase64))
                    });
                }
                else
                {
                    apiMessages.Add(new
                    {
                        role = msg.Role,
                        content = msg.Content
                    });
                }
            }

            return apiMessages;
        }
    }
}
