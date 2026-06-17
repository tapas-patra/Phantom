using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using SecureOverlay.Helpers;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Services
{
    public static class ByoProviderModelCatalogService
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(12);
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static void RefreshStaleCatalogs(AppSettings settings)
        {
            foreach (var provider in AIModelProviders())
            {
                var keys = GetKeysForProvider(settings, provider);
                if (keys.Count == 0)
                {
                    continue;
                }

                if (settings.ProviderModelCatalogRefreshedAtUtc.TryGetValue(provider, out var refreshedAtUtc)
                    && refreshedAtUtc > DateTime.UtcNow - RefreshInterval)
                {
                    continue;
                }

                try
                {
                    var models = FetchModels(provider, keys[0]);
                    if (models.Count == 0)
                    {
                        continue;
                    }

                    ProviderModelCatalogCache.UpsertProvider(settings, provider, provider, models, DateTime.UtcNow);
                    settings.ProviderModelCatalogRefreshedAtUtc[provider] = DateTime.UtcNow;
                    var currentModel = AIModelRegistry.GetCurrentModelForProvider(settings, provider);
                    if (!models.Any(item => string.Equals(item.ModelId, currentModel, StringComparison.OrdinalIgnoreCase)))
                    {
                        AIModelRegistry.SetModelForProvider(settings, provider, models[0].ModelId);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"BYO model refresh skipped for {provider}: {ex.Message}");
                }
            }
        }

        private static List<ManagedAiModelOptionDto> FetchModels(string provider, string apiKey)
        {
            return provider switch
            {
                "ChatGPT" => FetchOpenAiLikeModels("https://api.openai.com/v1/models", apiKey),
                "Claude" => FetchAnthropicModels(apiKey),
                "Gemini" => FetchGeminiModels(apiKey),
                "Mistral" => FetchOpenAiLikeModels("https://api.mistral.ai/v1/models", apiKey),
                "Groq" => FetchOpenAiLikeModels("https://api.groq.com/openai/v1/models", apiKey),
                "NVIDIA" => FetchOpenAiLikeModels("https://integrate.api.nvidia.com/v1/models", apiKey),
                _ => new List<ManagedAiModelOptionDto>()
            };
        }

        private static List<ManagedAiModelOptionDto> FetchOpenAiLikeModels(string url, string apiKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = HttpClient.SendAsync(request).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return new List<ManagedAiModelOptionDto>();
            }

            return data.EnumerateArray()
                .Select(item => item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null)
                .Where(id => !string.IsNullOrWhiteSpace(id) && LooksLikeChatModel(id!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .Select(id => new ManagedAiModelOptionDto
                {
                    ModelId = id!,
                    DisplayName = id!,
                    SupportsVision = ProviderModelCatalogCache.InferVisionSupport(id, null)
                })
                .ToList();
        }

        private static List<ManagedAiModelOptionDto> FetchAnthropicModels(string apiKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            using var response = HttpClient.SendAsync(request).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return new List<ManagedAiModelOptionDto>();
            }

            return data.EnumerateArray()
                .Select(item =>
                {
                    var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                    var displayName = item.TryGetProperty("display_name", out var displayNameElement) ? displayNameElement.GetString() : id;
                    return string.IsNullOrWhiteSpace(id)
                        ? null
                        : new ManagedAiModelOptionDto
                        {
                            ModelId = id,
                            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName!,
                            SupportsVision = ProviderModelCatalogCache.InferVisionSupport(id, displayName)
                        };
                })
                .Where(item => item != null)
                .GroupBy(item => item!.ModelId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()!)
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<ManagedAiModelOptionDto> FetchGeminiModels(string apiKey)
        {
            using var response = HttpClient.GetAsync(
                $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(apiKey)}")
                .GetAwaiter()
                .GetResult();
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            if (!document.RootElement.TryGetProperty("models", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return new List<ManagedAiModelOptionDto>();
            }

            return data.EnumerateArray()
                .Where(item => item.TryGetProperty("supportedGenerationMethods", out var methods)
                    && methods.ValueKind == JsonValueKind.Array
                    && methods.EnumerateArray().Any(method => string.Equals(method.GetString(), "generateContent", StringComparison.OrdinalIgnoreCase)))
                .Select(item =>
                {
                    var id = item.TryGetProperty("baseModelId", out var idElement) ? idElement.GetString() : null;
                    var displayName = item.TryGetProperty("displayName", out var displayNameElement) ? displayNameElement.GetString() : id;
                    return string.IsNullOrWhiteSpace(id)
                        ? null
                        : new ManagedAiModelOptionDto
                        {
                            ModelId = id,
                            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName!,
                            SupportsVision = ProviderModelCatalogCache.InferVisionSupport(id, displayName)
                        };
                })
                .Where(item => item != null)
                .GroupBy(item => item!.ModelId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()!)
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool LooksLikeChatModel(string modelId)
        {
            var normalized = modelId.ToLowerInvariant();
            if (normalized.Contains("embedding")
                || normalized.Contains("moderation")
                || normalized.Contains("whisper")
                || normalized.Contains("tts")
                || normalized.Contains("transcribe")
                || normalized.Contains("image")
                || normalized.Contains("rerank"))
            {
                return false;
            }

            return normalized.Contains("gpt")
                || normalized.StartsWith("o1")
                || normalized.StartsWith("o3")
                || normalized.Contains("claude")
                || normalized.Contains("mistral")
                || normalized.Contains("mixtral")
                || normalized.Contains("pixtral")
                || normalized.Contains("gemini")
                || normalized.Contains("llama")
                || normalized.Contains("nemotron")
                || normalized.Contains("gemma")
                || normalized.Contains("qwen")
                || normalized.Contains("deepseek")
                || normalized.Contains("kimi")
                || normalized.Contains("glm");
        }

        private static List<string> GetKeysForProvider(AppSettings settings, string provider)
        {
            return provider switch
            {
                "ChatGPT" => settings.ChatGPTApiKeys,
                "Claude" => settings.ClaudeApiKeys,
                "Mistral" => settings.MistralApiKeys,
                "Gemini" => settings.GeminiApiKeys,
                "Groq" => settings.GroqApiKeys,
                "NVIDIA" => settings.NvidiaApiKeys,
                _ => new List<string>()
            };
        }

        private static string[] AIModelProviders()
        {
            return new[]
            {
                "ChatGPT",
                "Claude",
                "Mistral",
                "Gemini",
                "Groq",
                "NVIDIA"
            };
        }
    }
}
