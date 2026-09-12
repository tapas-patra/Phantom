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
using SecureOverlay.Helpers;

namespace SecureOverlay.Services
{
    public class MistralService : IAIService
    {
        private readonly string _apiKey;
        private readonly string _model;
        private static readonly HttpClient _httpClient = new HttpClient();

        public MistralService(string apiKey, string model)
        {
            _apiKey = apiKey;
            _model = model;
        }

        public string GetProviderName() => "Mistral";

        public bool IsConfigured() => !string.IsNullOrWhiteSpace(_apiKey);

        // ✅ UPDATED: Non-streaming method with image support
        public async Task<string> SendMessageAsync(List<ConversationMessage> messages, IReadOnlyList<string>? imagesBase64 = null)
        {
            if (!IsConfigured())
                return "Error: Mistral API key not configured. Go to Settings.";
                
            var (shouldError, errorMsg) = ErrorSimulator.ShouldSimulateError();
            if (shouldError)
            {
                await Task.Delay(500);
                return errorMsg;
            }

            try
            {
                var apiMessages = new List<object>();

                foreach (var msg in messages.Where(m => !string.IsNullOrWhiteSpace(m.Content)))
                {
                    // ✅ Check if this is the last user message and has an image
                    bool isLastUserMessage = (msg == messages.Last(m => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content)));
                    
                    if (msg.Role == "user" && isLastUserMessage && MultimodalContentBuilder.HasImages(imagesBase64))
                    {
                        apiMessages.Add(new
                        {
                            role = "user",
                            content = MultimodalContentBuilder.BuildOpenAiContent(
                                msg.Content,
                                MultimodalContentBuilder.Normalize(imagesBase64),
                                mistralDirectUrl: true)
                        });
                    }
                    else
                    {
                        // Regular text message
                        apiMessages.Add(new
                        {
                            role = msg.Role,
                            content = msg.Content
                        });
                    }
                }

                if (apiMessages.Count == 0)
                {
                    return "Error: No valid messages to send";
                }

                var request = new
                {
                    model = _model,
                    messages = apiMessages,
                    max_tokens = ReasoningBudget.Resolve(messages).MaxTokens
                };

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.mistral.ai/v1/chat/completions")
                {
                    Content = content
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

        // ✅ UPDATED: Streaming method with image support
        public async Task<string> SendMessageStreamAsync(
            List<ConversationMessage> messages, 
            Action<string> onChunkReceived,
            CancellationToken cancellationToken = default,
            IReadOnlyList<string>? imagesBase64 = null)
        {
            if (!IsConfigured())
                return "Error: Mistral API key not configured. Go to Settings.";

            var (shouldError, errorMsg) = ErrorSimulator.ShouldSimulateError();
            if (shouldError)
            {
                await Task.Delay(500, cancellationToken);
                return errorMsg;
            }

            try
            {
                var apiMessages = new List<object>();

                foreach (var msg in messages.Where(m => !string.IsNullOrWhiteSpace(m.Content)))
                {
                    bool isLastUserMessage = (msg == messages.Last(m => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content)));
                    
                    if (msg.Role == "user" && isLastUserMessage && MultimodalContentBuilder.HasImages(imagesBase64))
                    {
                        apiMessages.Add(new
                        {
                            role = "user",
                            content = MultimodalContentBuilder.BuildOpenAiContent(
                                msg.Content,
                                MultimodalContentBuilder.Normalize(imagesBase64),
                                mistralDirectUrl: true)
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

                if (apiMessages.Count == 0)
                {
                    return "Error: No valid messages to send";
                }

                var request = new
                {
                    model = _model,
                    messages = apiMessages,
                    max_tokens = ReasoningBudget.Resolve(messages).MaxTokens,
                    stream = true
                };

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.mistral.ai/v1/chat/completions")
                {
                    Content = content
                };
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                var response = await _httpClient.SendAsync(
                    requestMessage,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                );

                if (!response.IsSuccessStatusCode)
                {
                    var errorJson = await response.Content.ReadAsStringAsync();
                    return $"Error: {response.StatusCode} - {errorJson}";
                }

                var fullResponse = new StringBuilder();
                var sawDone = false;

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new System.IO.StreamReader(stream))
                {
                    while (!reader.EndOfStream)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var line = await reader.ReadLineAsync();

                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        if (!line.StartsWith("data: "))
                            continue;

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
                                onChunkReceived?.Invoke(delta);
                            }
                        }
                        catch
                        {
                            // Ignore malformed chunks
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
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}
