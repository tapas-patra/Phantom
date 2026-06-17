using System;
using System.Collections.Generic;
using System.Linq;

namespace SecureOverlay.Services
{
    /// <summary>
    /// Manages API key and model rotation with intelligent error handling
    /// </summary>
    public class APIRotationManager
    {
        private readonly AppSettings _settings;
        private readonly APIRotationState _state;

        public bool IsAutoSwitchKeysEnabled => _settings.AutoSwitchKeysOnError;
        public bool IsAutoSwitchModelsEnabled => _settings.AutoSwitchModelsOnError;

        private Dictionary<string, int> _sessionModelIndex = new Dictionary<string, int>();
        private Dictionary<string, int> _conversationModelIndex = new Dictionary<string, int>();

        public APIRotationManager(AppSettings settings)
        {
            _settings = settings;
            _state = settings.RotationState;
        }

        // ═══════════════════════════════════════════════════════════════
        // API KEY ROTATION
        // ═══════════════════════════════════════════════════════════════

        public string GetNextApiKey(string provider)
        {
            var keys = GetKeysForProvider(provider);
            
            if (keys.Count == 0)
            {
                Log.WriteLine($"⚠️ No API keys configured for {provider}");
                return "";
            }

            if (keys.Count == 1)
            {
                Log.WriteLine($"Only 1 key for {provider} - using it");
                return keys[0];
            }

            var permanentlyFailedKeys = GetPermanentlyFailedKeyIndexes(provider);
            var failedKeys = GetFailedKeys(provider);
            
            // Get last used index
            if (!_state.LastKeyIndex.ContainsKey(provider))
            {
                _state.LastKeyIndex[provider] = -1;
            }

            int startIndex = _state.LastKeyIndex[provider];
            int currentIndex = startIndex;
            int attempts = 0;
            
            // Round-robin search for next available key (skip failed ones)
            while (attempts < keys.Count)
            {
                attempts++;
                currentIndex = (currentIndex + 1) % keys.Count;
                var key = keys[currentIndex];

                if (permanentlyFailedKeys.Contains(currentIndex))
                {
                    continue;
                }
                
                // Check if this key is not in failed list
                if (!failedKeys.Contains(key))
                {
                    _state.LastKeyIndex[provider] = currentIndex;
                    SaveRotationState();
                    
                    Log.WriteLine($"✓ Selected {provider} Key #{currentIndex + 1} (round-robin)");
                    return key;
                }
            }

            if (failedKeys.Count > 0)
            {
                Log.WriteLine($"⚠️ All non-invalid {provider} keys are rate limited - clearing temporary failures");
                ClearFailedKeys(provider);
                return GetNextApiKey(provider);
            }

            Log.WriteLine($"⚠️ No usable API keys remain for {provider}");
            return "";
        }

        public string GetCurrentApiKey(string provider)
        {
            var keys = GetKeysForProvider(provider);

            if (keys.Count == 0)
            {
                Log.WriteLine($"⚠️ No API keys configured for {provider}");
                return "";
            }

            if (keys.Count == 1)
            {
                return keys[0];
            }

            if (_state.LastKeyIndex.TryGetValue(provider, out var currentIndex) &&
                currentIndex >= 0 &&
                currentIndex < keys.Count)
            {
                var currentKey = keys[currentIndex];
                if (!GetPermanentlyFailedKeyIndexes(provider).Contains(currentIndex) &&
                    !GetFailedKeys(provider).Contains(currentKey))
                {
                    return currentKey;
                }
            }

            return GetNextApiKey(provider);
        }

        /// <summary>
        /// Mark a specific key as failed
        /// </summary>
        public void MarkKeyAsFailed(string provider, int keyIndex)
        {
            var failedKey = $"{provider}_Failed";
            var property = _settings.GetType().GetProperty(failedKey);
            
            if (property != null)
            {
                var failedKeys = property.GetValue(_settings) as List<int> ?? new List<int>();
                
                if (!failedKeys.Contains(keyIndex))
                {
                    failedKeys.Add(keyIndex);
                    property.SetValue(_settings, failedKeys);
                    SaveRotationState();
                    Log.WriteLine($"✓ Marked Key #{keyIndex + 1} as failed for {provider}");
                }
            }
        }

        public void MarkKeyAsRateLimited(string provider, int keyIndex)
        {
            var keys = GetKeysForProvider(provider);
            if (keyIndex < 0 || keyIndex >= keys.Count)
            {
                Log.WriteLine($"⚠️ Cannot mark rate-limited key for {provider}: invalid index {keyIndex}");
                return;
            }

            var key = keys[keyIndex];
            var failedKeys = GetFailedKeys(provider);

            if (!failedKeys.Contains(key))
            {
                failedKeys.Add(key);
                _state.Last429Time[provider] = DateTime.UtcNow;
                SaveRotationState();
                Log.WriteLine($"✓ Marked Key #{keyIndex + 1} as rate limited for {provider}");
            }
        }

        private List<string> GetFailedKeys(string provider)
        {
            if (!_state.FailedKeys429.ContainsKey(provider))
            {
                _state.FailedKeys429[provider] = new List<string>();
            }
            return _state.FailedKeys429[provider];
        }

        private void ClearFailedKeys(string provider)
        {
            if (_state.FailedKeys429.ContainsKey(provider))
            {
                _state.FailedKeys429[provider].Clear();
                SaveRotationState();
            }
        }

        public void ClearRateLimitedKeys(string provider)
        {
            ClearFailedKeys(provider);
        }

        // ═══════════════════════════════════════════════════════════════
        // MODEL ROTATION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Get next model and update settings to persist the change
        /// </summary>
        public string GetNextModel(string provider)
        {
            var models = GetModelsForProvider(provider);
            
            if (models.Count <= 1)
                return models.FirstOrDefault() ?? "";
            
            // Get current conversation index
            int currentIndex = _conversationModelIndex.ContainsKey(provider) 
                ? _conversationModelIndex[provider] 
                : 0;
            
            // Move to next model
            int newIndex = (currentIndex + 1) % models.Count;
            
            // Save to conversation (not persisted to disk)
            _conversationModelIndex[provider] = newIndex;
            
            // ✅ Update settings object so UI reflects current model
            UpdateSettingsModel(provider, models[newIndex]);
            
            // ✅ **NEW: Save settings to disk so Settings page shows correct model**
            SaveRotationState();
            
            Log.WriteLine($"✓ Switched {provider} to model #{newIndex + 1}: {models[newIndex]} (for this conversation)");
            return models[newIndex];
        }

        
        private void UpdateSettingsModel(string provider, string model)
        {
            switch (provider)
            {
                case "ChatGPT":
                    _settings.ChatGPTModel = model;
                    break;
                case "Claude":
                    _settings.ClaudeModel = model;
                    break;
                case "Mistral":
                    _settings.MistralModel = model;
                    break;
                case "Gemini":
                    _settings.GeminiModel = model;
                    break;
                case "Groq":
                    _settings.GroqModel = model;
                    break;
            }
            
            Log.WriteLine($"✓ Settings updated: {provider} model set to {model}");
        }

        public void ResetSessionModelIndex(string provider)
        {
            if (_sessionModelIndex.ContainsKey(provider))
            {
                _sessionModelIndex.Remove(provider);
                Log.WriteLine($"✓ Reset session model index for {provider}");
            }
        }

        /// <summary>
        /// Manually set the current model (for user selection from UI)
        /// Updates both conversation index and settings
        /// </summary>
        public void SetCurrentModel(string provider, string model)
        {
            var models = GetModelsForProvider(provider);
            
            if (models == null || models.Count == 0)
            {
                Log.WriteLine($"⚠️ No models configured for {provider}");
                return;
            }
            
            var modelIndex = models.IndexOf(model);
            
            if (modelIndex >= 0)
            {
                // Update conversation-level index (persists for this conversation)
                _conversationModelIndex[provider] = modelIndex;
                
                // Also update settings so UI reflects the change
                UpdateSettingsModel(provider, model);
                
                // ✅ **NEW: Save to disk so Settings page stays in sync**
                SaveRotationState();
                
                Log.WriteLine($"✓ Manually set {provider} to model: {model} (index {modelIndex})");
                Log.WriteLine($"   This model will persist for the current conversation");
            }
            else
            {
                Log.WriteLine($"⚠️ Model '{model}' not found for provider {provider}");
                
                // Log available models for debugging
                Log.WriteLine($"Available models for {provider}:");
                for (int i = 0; i < models.Count; i++)
                {
                    Log.WriteLine($"  [{i}] {models[i]}");
                }
            }
        }


        public string GetCurrentModel(string provider)
        {
            var models = GetModelsForProvider(provider);
            
            if (models.Count == 0)
                return "";

            // Use conversation-level index if set, otherwise use settings default
            int index = 0;
            
            if (_conversationModelIndex.ContainsKey(provider))
            {
                index = _conversationModelIndex[provider];
                
                // ✅ NEW: Also sync to settings
                if (index < models.Count)
                {
                    UpdateSettingsModel(provider, models[index]);
                }
                
                Log.WriteLine($"Using conversation model index {index} for {provider}");
            }
            else
            {
                // Get index from settings model
                var settingsModel = GetSettingsModel(provider);
                index = models.IndexOf(settingsModel);
                if (index < 0) index = 0;
                
                Log.WriteLine($"No conversation model set - using settings model (index {index}) for {provider}");
            }

            if (index >= models.Count)
                index = 0;

            return models[index];
        }

        // ═══════════════════════════════════════════════════════════════
        // ERROR ANALYSIS
        // ═══════════════════════════════════════════════════════════════

        public bool Is429Error(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
                return false;

            var errorLower = errorMessage.ToLower();
            
            // Generic 429 detection across all providers
            return errorLower.Contains("429") ||
                   errorLower.Contains("rate_limit") ||
                   errorLower.Contains("ratelimit") ||
                   errorLower.Contains("too many requests") ||
                   errorLower.Contains("quota") ||
                   errorLower.Contains("resource_exhausted");
        }

        public bool IsRetryableError(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
                return false;

            var errorLower = errorMessage.ToLower();
            
            // Errors that are worth retrying
            return errorLower.Contains("timeout") ||
                   errorLower.Contains("503") ||
                   errorLower.Contains("502") ||
                   errorLower.Contains("500") ||
                   errorLower.Contains("overloaded") ||
                   errorLower.Contains("capacity") ||
                   errorLower.Contains("network");
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPER METHODS
        // ═══════════════════════════════════════════════════════════════
        // ✅ NEW: Helper to get settings model
        private string GetSettingsModel(string provider)
        {
            return provider switch
            {
                "ChatGPT" => _settings.ChatGPTModel,
                "Claude" => _settings.ClaudeModel,
                "Mistral" => _settings.MistralModel,
                "Gemini" => _settings.GeminiModel,
                "Groq" => _settings.GroqModel,
                _ => ""
            };
        }

        private List<int> GetPermanentlyFailedKeyIndexes(string provider)
        {
            var failedKey = $"{provider}_Failed";
            return _settings.GetType()
                .GetProperty(failedKey)
                ?.GetValue(_settings) as List<int> ?? new List<int>();
        }

        private List<string> GetKeysForProvider(string provider)
        {
            return provider switch
            {
                "ChatGPT" => _settings.ChatGPTApiKeys,
                "Claude" => _settings.ClaudeApiKeys,
                "Mistral" => _settings.MistralApiKeys,
                "Gemini" => _settings.GeminiApiKeys,
                "Groq" => _settings.GroqApiKeys,
                _ => new List<string>()
            };
        }

        private List<string> GetModelsForProvider(string provider)
        {
            return provider switch
            {
                "ChatGPT" => _settings.ChatGPTModels,
                "Claude" => _settings.ClaudeModels,
                "Mistral" => _settings.MistralModels,
                "Gemini" => _settings.GeminiModels,
                "Groq" => _settings.GroqModels,
                _ => new List<string>()
            };
        }

        private void SaveRotationState()
        {
            try
            {
                SettingsManager.Save(_settings);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error saving rotation state: {ex.Message}");
            }
        }

        /// <summary>
        /// Get number of available (non-failed) keys
        /// </summary>
        public int GetAvailableKeyCount(string provider)
        {
            var keys = GetKeysForProvider(provider);
            if (keys == null || keys.Count == 0) return 0;

            var permanentlyFailedKeys = GetPermanentlyFailedKeyIndexes(provider);
            var rateLimitedKeys = GetFailedKeys(provider);
            int available = 0;

            for (int i = 0; i < keys.Count; i++)
            {
                if (permanentlyFailedKeys.Contains(i))
                    continue;

                if (rateLimitedKeys.Contains(keys[i]))
                    continue;

                available++;
            }

            return available;
        }

        public int GetRecoverableKeyCount(string provider)
        {
            var keys = GetKeysForProvider(provider);
            if (keys == null || keys.Count == 0) return 0;

            var permanentlyFailedKeys = GetPermanentlyFailedKeyIndexes(provider);
            return Math.Max(0, keys.Count - permanentlyFailedKeys.Count);
        }

        /// <summary>
        /// Get total number of keys for a provider
        /// </summary>
        public int GetTotalKeyCount(string provider)
        {
            var keys = GetKeysForProvider(provider);
            return keys?.Count ?? 0;
        }

        public bool HasMultipleKeys(string provider)
        {
            return GetKeysForProvider(provider).Count > 1;
        }

        /// <summary>
        /// Check if provider has multiple models configured
        /// </summary>
        public bool HasMultipleModels(string provider)
        {
            var models = GetModelsForProvider(provider);
            var result = models != null && models.Count > 1;
            Log.WriteLine($"HasMultipleModels({provider}): {result} ({models?.Count ?? 0} models)");
            return result;
        }

        /// <summary>
        /// Get current key index
        /// </summary>
        public int GetCurrentKeyIndex(string provider)
        {
            if (_state.LastKeyIndex.ContainsKey(provider))
            {
                return _state.LastKeyIndex[provider];
            }
            return 0;
        }


        public string GetCurrentKeyInfo(string provider)
        {
            var keys = GetKeysForProvider(provider);
            if (keys.Count == 0)
                return "No keys";
            
            if (keys.Count == 1)
                return "Key #1";
            
            var index = GetCurrentKeyIndex(provider);
            var failedKeys = GetFailedKeys(provider);
            var availableCount = keys.Count - failedKeys.Count;
            
            return $"Key #{index + 1}/{keys.Count} ({availableCount} available)";
        }

        /// <summary>
        /// Reset key rotation to start from first key again (useful when switching models)
        /// </summary>
        public void ResetKeyRotation(string provider)
        {
            if (_state.LastKeyIndex.ContainsKey(provider))
            {
                _state.LastKeyIndex[provider] = -1; // Will be incremented to 0 on next GetNextApiKey
                SaveRotationState();
                Log.WriteLine($"✓ Reset key rotation for {provider} - will start from Key #1");
            }
            else
            {
                _state.LastKeyIndex[provider] = -1;
                SaveRotationState();
                Log.WriteLine($"✓ Initialized key rotation for {provider}");
            }
        }

        /// <summary>
        /// Call this when starting a new conversation (New Topic, Clear Chat, App Start)
        /// </summary>
        public void StartNewConversation(string provider)
        {
            if (_conversationModelIndex.ContainsKey(provider))
            {
                _conversationModelIndex.Remove(provider);
                Log.WriteLine($"✓ New conversation - reset {provider} to preferred model");
            }
        }
        
        /// <summary>
        /// Call this when changing providers
        /// </summary>
        public void ResetAllConversationState()
        {
            _conversationModelIndex.Clear();
            Log.WriteLine($"✓ Reset all conversation model preferences");
        }

    }
}
