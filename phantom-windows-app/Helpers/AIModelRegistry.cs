using System.Collections.Generic;
using System.Linq;
using SecureOverlay.Services;

namespace SecureOverlay.Helpers
{
    /// <summary>
    /// Central registry for all AI providers, models, and their configurations.
    /// UPDATE THIS FILE to add new providers or models - changes propagate everywhere!
    /// </summary>
    public static class AIModelRegistry
    {
        // ═══════════════════════════════════════════════════════════════
        // PROVIDER DEFINITIONS
        // ═══════════════════════════════════════════════════════════════
        
        public static class Providers
        {
            public const string ChatGPT = "ChatGPT";
            public const string Claude = "Claude";
            public const string Mistral = "Mistral";
            public const string Gemini = "Gemini";
            public const string Groq = "Groq";
            public const string Nvidia = "NVIDIA";
        }

        public static string[] GetAllProviders()
        {
            return new[] 
            { 
                Providers.ChatGPT, 
                Providers.Claude, 
                Providers.Mistral, 
                Providers.Gemini, 
                Providers.Groq,
                Providers.Nvidia
            };
        }

        // ═══════════════════════════════════════════════════════════════
        // MODEL DEFINITIONS (grouped by provider)
        // ═══════════════════════════════════════════════════════════════
        
        private static readonly Dictionary<string, List<ModelInfo>> _modelsByProvider = new Dictionary<string, List<ModelInfo>>
        {
            [Providers.ChatGPT] = new List<ModelInfo>
            {
                new ModelInfo
                {
                    Id = "gpt-4o",
                    DisplayName = "GPT-4o",
                    MaxContextTokens = 128000,
                    MaxResponseTokens = 4000,
                    SlidingWindowSize = 15,
                    SupportsVision = true
                },
                new ModelInfo
                {
                    Id = "gpt-4o-mini",
                    DisplayName = "GPT-4o Mini",
                    MaxContextTokens = 128000,
                    MaxResponseTokens = 4000,
                    SlidingWindowSize = 15,
                    SupportsVision = true
                },
                new ModelInfo
                {
                    Id = "gpt-4-turbo",
                    DisplayName = "GPT-4 Turbo",
                    MaxContextTokens = 128000,
                    MaxResponseTokens = 4000,
                    SlidingWindowSize = 15,
                    SupportsVision = true
                },
                new ModelInfo
                {
                    Id = "gpt-4-vision-preview",
                    DisplayName = "GPT-4 Vision",
                    MaxContextTokens = 128000,
                    MaxResponseTokens = 4000,
                    SlidingWindowSize = 15,
                    SupportsVision = true
                },
                new ModelInfo
                {
                    Id = "gpt-4",
                    DisplayName = "GPT-4",
                    MaxContextTokens = 8000,
                    MaxResponseTokens = 2000,
                    SlidingWindowSize = 5,
                    SupportsVision = false
                },
                new ModelInfo
                {
                    Id = "gpt-3.5-turbo",
                    DisplayName = "GPT-3.5 Turbo",
                    MaxContextTokens = 4000,
                    MaxResponseTokens = 1000,
                    SlidingWindowSize = 3,
                    SupportsVision = false
                }
            },

            [Providers.Claude] = new List<ModelInfo>
            {
                new ModelInfo
                {
                    Id = "claude-3-5-sonnet-20241022",
                    DisplayName = "Claude 3.5 Sonnet",
                    MaxContextTokens = 200000,
                    MaxResponseTokens = 4000,
                    SlidingWindowSize = 15,
                    SupportsVision = true
                },
                new ModelInfo
                {
                    Id = "claude-3-5-sonnet-20240620",
                    DisplayName = "Claude 3.5 Sonnet (Jun)",
                    MaxContextTokens = 200000,
                    MaxResponseTokens = 4000,
                    SlidingWindowSize = 15,
                    SupportsVision = true
                },
                new ModelInfo
                {
                    Id = "claude-3-opus-20240229",
                    DisplayName = "Claude 3 Opus",
                    MaxContextTokens = 200000,
                    MaxResponseTokens = 4000,
                    SlidingWindowSize = 15,
                    SupportsVision = true
                },
                new ModelInfo
                {
                    Id = "claude-3-sonnet-20240229",
                    DisplayName = "Claude 3 Sonnet",
                    MaxContextTokens = 200000,
                    MaxResponseTokens = 4000,
                    SlidingWindowSize = 15,
                    SupportsVision = true
                },
                new ModelInfo
                {
                    Id = "claude-3-haiku-20240307",
                    DisplayName = "Claude 3 Haiku",
                    MaxContextTokens = 200000,
                    MaxResponseTokens = 4000,
                    SlidingWindowSize = 15,
                    SupportsVision = true
                }
            },

            [Providers.Mistral] = new List<ModelInfo>
            {
                new ModelInfo
                {
                    Id = "mistral-large-latest",
                    DisplayName = "Mistral Large",
                    MaxContextTokens = 32000,
                    MaxResponseTokens = 2000,
                    SlidingWindowSize = 10,
                    SupportsVision = false
                },
                new ModelInfo
                {
                    Id = "mistral-medium-latest",
                    DisplayName = "Mistral Medium",
                    MaxContextTokens = 32000,
                    MaxResponseTokens = 2000,
                    SlidingWindowSize = 10,
                    SupportsVision = false
                },
                new ModelInfo
                {
                    Id = "mistral-small-latest",
                    DisplayName = "Mistral Small",
                    MaxContextTokens = 32000,
                    MaxResponseTokens = 2000,
                    SlidingWindowSize = 10,
                    SupportsVision = false
                }
            },

            [Providers.Gemini] = new List<ModelInfo>
            {
                new ModelInfo
                {
                    Id = "gemini-2.5-pro",
                    DisplayName = "Gemini 2.5 Pro",
                    MaxContextTokens = 2000000,
                    MaxResponseTokens = 8192,
                    SlidingWindowSize = 25,
                    SupportsVision = true
                },
                new ModelInfo
                {
                    Id = "gemini-2.5-flash",
                    DisplayName = "Gemini 2.5 Flash",
                    MaxContextTokens = 1000000,
                    MaxResponseTokens = 8192,
                    SlidingWindowSize = 20,
                    SupportsVision = true
                },
                new ModelInfo
                {
                    Id = "gemini-2.0-flash-exp",
                    DisplayName = "Gemini 2.0 Flash",
                    MaxContextTokens = 1000000,
                    MaxResponseTokens = 8192,
                    SlidingWindowSize = 20,
                    SupportsVision = true
                },
            },

            [Providers.Groq] = new List<ModelInfo>
            {
                new ModelInfo
                {
                    Id = "llama-3.3-70b-versatile",
                    DisplayName = "Llama 3.3 70B",
                    MaxContextTokens = 128000,
                    MaxResponseTokens = 8000,
                    SlidingWindowSize = 15,
                    SupportsVision = false
                },
                new ModelInfo
                {
                    Id = "llama-3.1-8b-instant",
                    DisplayName = "Llama 3.1 8B",
                    MaxContextTokens = 128000,
                    MaxResponseTokens = 8000,
                    SlidingWindowSize = 15,
                    SupportsVision = false
                },
                new ModelInfo
                {
                    Id = "meta-llama/llama-4-scout-17b-16e-instruct",
                    DisplayName = "Llama 4 Scout",
                    MaxContextTokens = 10000,
                    MaxResponseTokens = 4096,
                    SlidingWindowSize = 8,
                    SupportsVision = true  // Has vision support
                },
                new ModelInfo
                {
                    Id = "groq/compound-mini",
                    DisplayName = "Compound Mini",
                    MaxContextTokens = 8192,
                    MaxResponseTokens = 4096,
                    SlidingWindowSize = 8,
                    SupportsVision = false
                },
                new ModelInfo
                {
                    Id = "qwen/qwen3-32b",
                    DisplayName = "Qwen3 32B",
                    MaxContextTokens = 32768,
                    MaxResponseTokens = 8000,
                    SlidingWindowSize = 12,
                    SupportsVision = false
                }
            },
            [Providers.Nvidia] = new List<ModelInfo>
            {
            }
        };

        // ═══════════════════════════════════════════════════════════════
        // PUBLIC API - USE THESE METHODS THROUGHOUT THE APP
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Get all model IDs for a provider
        /// </summary>
        public static string[] GetModelsForProvider(string provider)
        {
            if (_modelsByProvider.ContainsKey(provider))
            {
                return _modelsByProvider[provider].Select(m => m.Id).ToArray();
            }
            return new string[0];
        }

        /// <summary>
        /// Get display name for a model ID
        /// </summary>
        public static string GetDisplayName(string modelId)
        {
            foreach (var models in _modelsByProvider.Values)
            {
                var model = models.FirstOrDefault(m => m.Id == modelId);
                if (model != null)
                    return model.DisplayName;
            }
            return modelId; // Fallback to ID if not found
        }

        /// <summary>
        /// Check if a model supports vision
        /// </summary>
        public static bool SupportsVision(string modelId)
        {
            foreach (var models in _modelsByProvider.Values)
            {
                var model = models.FirstOrDefault(m => m.Id == modelId);
                if (model != null)
                    return model.SupportsVision;
            }
            return false;
        }

        /// <summary>
        /// Get full ModelInfo object for a model
        /// </summary>
        public static ModelInfo? GetModelInfo(string modelId)
        {
            foreach (var models in _modelsByProvider.Values)
            {
                var model = models.FirstOrDefault(m => m.Id == modelId);
                if (model != null)
                    return model;
            }
            return null;
        }

        /// <summary>
        /// Get ModelConfig for ConversationManager (converts ModelInfo to ModelConfig)
        /// </summary>
        public static ModelConfig GetModelConfig(string modelId)
        {
            var info = GetModelInfo(modelId);
            if (info != null)
            {
                return new ModelConfig
                {
                    Name = info.DisplayName,
                    MaxContextTokens = info.MaxContextTokens,
                    MaxResponseTokens = info.MaxResponseTokens,
                    SlidingWindowSize = info.SlidingWindowSize
                };
            }

            // Fallback default
            return new ModelConfig
            {
                Name = "Unknown",
                MaxContextTokens = 4000,
                MaxResponseTokens = 1000,
                SlidingWindowSize = 3
            };
        }

        /// <summary>
        /// Get current model from settings for a provider
        /// </summary>
        public static string GetCurrentModelForProvider(AppSettings settings, string provider)
        {
            return provider switch
            {
                "ChatGPT" => settings.ChatGPTModel,
                "Claude" => settings.ClaudeModel,
                "Mistral" => settings.MistralModel,
                "Gemini" => settings.GeminiModel,
                "Groq" => settings.GroqModel,
                "NVIDIA" => settings.NvidiaModel,
                _ => GetModelsForProvider(provider).FirstOrDefault() ?? ""
            };
        }

        /// <summary>
        /// Set model in settings for a provider
        /// </summary>
        public static void SetModelForProvider(AppSettings settings, string provider, string model)
        {
            switch (provider)
            {
                case "ChatGPT":
                    settings.ChatGPTModel = model;
                    break;
                case "Claude":
                    settings.ClaudeModel = model;
                    break;
                case "Mistral":
                    settings.MistralModel = model;
                    break;
                case "Gemini":
                    settings.GeminiModel = model;
                    break;
                case "Groq":
                    settings.GroqModel = model;
                    break;
                case "NVIDIA":
                    settings.NvidiaModel = model;
                    break;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // MODEL INFO CLASS
    // ═══════════════════════════════════════════════════════════════
    
    public class ModelInfo
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int MaxContextTokens { get; set; }
        public int MaxResponseTokens { get; set; }
        public int SlidingWindowSize { get; set; }
        public bool SupportsVision { get; set; }
    }

}
