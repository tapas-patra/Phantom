using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SecureOverlay.Helpers;

namespace SecureOverlay.Services
{
    public class GeminiService : IAIService
    {
        private readonly string _apiKey;
        private readonly string _model;
        private static readonly HttpClient _httpClient = new HttpClient();

        public GeminiService(string apiKey, string model)
        {
            _apiKey = apiKey;
            _model = model;
        }

        public string GetProviderName() => "Gemini";

        public bool IsConfigured() => !string.IsNullOrWhiteSpace(_apiKey);

        // Non-streaming method
        public async Task<string> SendMessageAsync(List<ConversationMessage> messages,  IReadOnlyList<string>? imagesBase64 = null)
        {
            if (!IsConfigured())
                return "Error: Gemini API key not configured. Go to Settings.";

            // ═══════════════════════════════════════════════════════════════
            // NEW: TEST MODE - Simulate errors
            // ═══════════════════════════════════════════════════════════════
            var (shouldError, errorMsg) = ErrorSimulator.ShouldSimulateError();
            if (shouldError)
            {
                // Simulate a brief delay to mimic real API call
                await Task.Delay(500);
                return errorMsg;
            }

            try
            {
                // Gemini uses a different message format
                var contents = ConvertMessagesToGeminiFormat(messages, imagesBase64);

                var request = new
                {
                    contents = contents,
                    generationConfig = new
                    {
                        maxOutputTokens = 2000,
                        temperature = 0.7
                    }
                };

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Gemini uses API key in URL, not header
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

                var response = await _httpClient.PostAsync(url, content);
                var responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return $"Error: {response.StatusCode} - {responseJson}";
                }

                dynamic? result = JsonConvert.DeserializeObject(responseJson);
                
                // Gemini response format: candidates[0].content.parts[0].text
                return result?.candidates[0]?.content?.parts[0]?.text?.ToString() ?? "No response";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        // Streaming method
        public async Task<string> SendMessageStreamAsync(
            List<ConversationMessage> messages, 
            Action<string> onChunkReceived,
            CancellationToken cancellationToken = default,
            IReadOnlyList<string>? imagesBase64 = null)
        {
            if (!IsConfigured())
                return "Error: Gemini API key not configured. Go to Settings.";

            // ═══════════════════════════════════════════════════════════════
            // NEW: TEST MODE - Simulate errors
            // ═══════════════════════════════════════════════════════════════
            var (shouldError, errorMsg) = ErrorSimulator.ShouldSimulateError();
            if (shouldError)
            {
                // Simulate a brief delay to mimic real API call
                await Task.Delay(500, cancellationToken);
                return errorMsg;
            }

            try
            {
                var contents = ConvertMessagesToGeminiFormat(messages, imagesBase64);
                var plan = ReasoningBudget.Resolve(messages);
                var includeThinking = ReasoningBudget.SupportsNativeThinking("Gemini", _model) && plan.GeminiThinkingTokens > 0;
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:streamGenerateContent?key={_apiKey}&alt=sse";
                var response = await ReasoningBudget.SendWithOptionalThinkingAsync(
                    _httpClient,
                    withThinking =>
                    {
                        object payload = withThinking
                            ? new
                            {
                                contents,
                                generationConfig = new
                                {
                                    maxOutputTokens = plan.MaxTokens,
                                    temperature = 0.7,
                                    thinkingConfig = new { thinkingBudget = plan.GeminiThinkingTokens, includeThoughts = false }
                                }
                            }
                            : new
                            {
                                contents,
                                generationConfig = new
                                {
                                    maxOutputTokens = plan.MaxTokens,
                                    temperature = 0.7
                                }
                            };
                        return new HttpRequestMessage(HttpMethod.Post, url)
                        {
                            Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
                        };
                    },
                    includeThinking,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorJson = await response.Content.ReadAsStringAsync();
                    return $"Error: {response.StatusCode} - {errorJson}";
                }

                var fullResponse = new StringBuilder();
                var completed = false;

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream))
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

                        try
                        {
                            dynamic? chunk = JsonConvert.DeserializeObject(data);
                            var finishReason = chunk?.candidates[0]?.finishReason?.ToString();
                            if (string.Equals(finishReason, "STOP", StringComparison.OrdinalIgnoreCase)) completed = true;
                            var delta = chunk?.candidates[0]?.content?.parts[0]?.text?.ToString();

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

                if (!completed) return "Error: provider_stream_incomplete";
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

        private List<object> ConvertMessagesToGeminiFormat(List<ConversationMessage> messages, IReadOnlyList<string>? imagesBase64 = null)
        {
            var contents = new List<object>();
            string systemPrompt = "";

            var systemMsg = messages.FirstOrDefault(m => m.Role == "system");
            if (systemMsg != null)
            {
                systemPrompt = systemMsg.Content + "\n\n";
            }

            bool isFirstUserMessage = true;
            var userMessages = messages.Where(m => m.Role != "system" && !string.IsNullOrWhiteSpace(m.Content)).ToList();
            
            foreach (var (msg, index) in userMessages.Select((m, i) => (m, i)))
            {
                var role = msg.Role == "assistant" ? "model" : "user";
                var text = msg.Content;

                if (isFirstUserMessage && role == "user" && !string.IsNullOrEmpty(systemPrompt))
                {
                    text = systemPrompt + text;
                    isFirstUserMessage = false;
                }

                // Check if this is the last user message and has image
                bool isLastUserMessage = (index == userMessages.Count - 1 && role == "user");
                
                if (isLastUserMessage && MultimodalContentBuilder.HasImages(imagesBase64))
                {
                    contents.Add(new
                    {
                        role = role,
                        parts = MultimodalContentBuilder.BuildGeminiParts(
                            text,
                            MultimodalContentBuilder.Normalize(imagesBase64))
                    });
                }
                else
                {
                    contents.Add(new
                    {
                        role = role,
                        parts = new[] { new { text = text } }
                    });
                }
            }

            return contents;
        }
    }
}
