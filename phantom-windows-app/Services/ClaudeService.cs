using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SecureOverlay.Services
{
    public class ClaudeService : IAIService
    {
        private readonly string _apiKey;
        private readonly string _model;
        private static readonly HttpClient _httpClient = new HttpClient();

        public ClaudeService(string apiKey, string model)
        {
            _apiKey = apiKey;
            _model = model;
        }

        public string GetProviderName() => "Claude";

        public bool IsConfigured() => !string.IsNullOrWhiteSpace(_apiKey);

        public async Task<string> SendMessageAsync(List<ConversationMessage> messages, string? imageBase64 = null)
        {
            if (!IsConfigured())
                return "Error: Claude API key not configured. Go to Settings.";
                
            var (shouldError, errorMsg) = ErrorSimulator.ShouldSimulateError();
            if (shouldError)
            {
                await Task.Delay(500);
                return errorMsg;
            }

            try
            {
                var systemPrompt = messages.FirstOrDefault(m => m.Role == "system")?.Content ?? "";
                
                var apiMessages = new List<object>();

                foreach (var msg in messages.Where(m => m.Role != "system" && !string.IsNullOrWhiteSpace(m.Content)))
                {
                    bool isLastUserMessage = (msg == messages.Last(m => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content)));
                    
                    if (msg.Role == "user" && isLastUserMessage && !string.IsNullOrEmpty(imageBase64))
                    {
                        // Claude format: content array with text and image
                        apiMessages.Add(new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { 
                                    type = "image", 
                                    source = new { 
                                        type = "base64", 
                                        media_type = "image/png", 
                                        data = imageBase64 
                                    } 
                                },
                                new { type = "text", text = msg.Content }
                            }
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

                var request = new
                {
                    model = _model,
                    max_tokens = 2000,
                    system = systemPrompt,
                    messages = apiMessages
                };

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
                {
                    Content = content
                };
                requestMessage.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
                requestMessage.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

                var response = await _httpClient.SendAsync(requestMessage);
                var responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return $"Error: {response.StatusCode} - {responseJson}";
                }

                dynamic? result = JsonConvert.DeserializeObject(responseJson);
                return result?.content[0]?.text?.ToString() ?? "No response";
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
            string? imageBase64 = null)
        {
            if (!IsConfigured())
                return "Error: Claude API key not configured. Go to Settings.";

            var (shouldError, errorMsg) = ErrorSimulator.ShouldSimulateError();
            if (shouldError)
            {
                await Task.Delay(500, cancellationToken);
                return errorMsg;
            }

            try
            {
                var systemPrompt = messages.FirstOrDefault(m => m.Role == "system")?.Content ?? "";
                
                var apiMessages = new List<object>();

                foreach (var msg in messages.Where(m => m.Role != "system" && !string.IsNullOrWhiteSpace(m.Content)))
                {
                    bool isLastUserMessage = (msg == messages.Last(m => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content)));
                    
                    if (msg.Role == "user" && isLastUserMessage && !string.IsNullOrEmpty(imageBase64))
                    {
                        apiMessages.Add(new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { 
                                    type = "image", 
                                    source = new { 
                                        type = "base64", 
                                        media_type = "image/png", 
                                        data = imageBase64 
                                    } 
                                },
                                new { type = "text", text = msg.Content }
                            }
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

                var request = new
                {
                    model = _model,
                    max_tokens = 2000,
                    system = systemPrompt,
                    messages = apiMessages,
                    stream = true
                };

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
                {
                    Content = content
                };
                requestMessage.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
                requestMessage.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

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
                            break;

                        try
                        {
                            dynamic? chunk = JsonConvert.DeserializeObject(data);
                            var eventType = chunk?.type?.ToString();
                            
                            if (eventType == "content_block_delta")
                            {
                                var delta = chunk?.delta?.text?.ToString();
                                
                                if (!string.IsNullOrEmpty(delta))
                                {
                                    fullResponse.Append(delta);
                                    onChunkReceived?.Invoke(delta);
                                }
                            }
                        }
                        catch
                        {
                            // Ignore malformed chunks
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
