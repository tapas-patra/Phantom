using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Helpers;
using SecureOverlay.Infrastructure.Persistence;
using SecureOverlay.Infrastructure.Hosted.Contracts;
using SecureOverlay.Platform.Windows;
using SecureOverlay.Platform.Windows.Secrets;

namespace SecureOverlay.Services
{
    public class AppSettings
    {
        public string SelectedAI { get; set; } = "ChatGPT";
        
        // ═══════════════════════════════════════════════════════════════
        // Multiple API Keys per Provider
        // ═══════════════════════════════════════════════════════════════
        public List<string> ChatGPTApiKeys { get; set; } = new List<string>();
        public List<string> ClaudeApiKeys { get; set; } = new List<string>();
        public List<string> MistralApiKeys { get; set; } = new List<string>();
        public List<string> GeminiApiKeys { get; set; } = new List<string>();
        public List<string> GroqApiKeys { get; set; } = new List<string>();
        public List<string> NvidiaApiKeys { get; set; } = new List<string>();
        
        // Legacy single keys (for backward compatibility - auto-migrated)
        public string ChatGPTApiKey { get; set; } = "";
        public string ClaudeApiKey { get; set; } = "";
        public string MistralApiKey { get; set; } = "";
        public string GeminiApiKey { get; set; } = "";
        public string GroqApiKey { get; set; } = "";
        public string NvidiaApiKey { get; set; } = "";
        
        // Models (lists for model switching)
        public List<string> ChatGPTModels { get; set; } = new List<string> { "gpt-4", "gpt-4-turbo", "gpt-3.5-turbo" };
        public List<string> ClaudeModels { get; set; } = new List<string> { "claude-3-sonnet-20240229", "claude-3-haiku-20240307" };
        public List<string> MistralModels { get; set; } = new List<string> { "mistral-large-latest", "mistral-medium-latest" };
        public List<string> GeminiModels { get; set; } = new List<string> { "gemini-2.5-flash", "gemini-2.0-flash" };
        public List<string> GroqModels { get; set; } = new List<string> { "llama-3.3-70b-versatile", "llama-3.1-8b-instant", "meta-llama/llama-4-scout-17b-16e-instruct" };
        public List<string> NvidiaModels { get; set; } = new List<string>();
        
        // Legacy single models (for backward compatibility)
        public string ChatGPTModel { get; set; } = "gpt-4";
        public string ClaudeModel { get; set; } = "claude-3-sonnet-20240229";
        public string MistralModel { get; set; } = "mistral-large-latest";
        public string GeminiModel { get; set; } = "gemini-2.5-flash";
        public string GroqModel { get; set; } = "llama-3.3-70b-versatile";
        public string NvidiaModel { get; set; } = "";
        
        // Rotation settings
        public bool AutoSwitchKeysOnError { get; set; } = true;
        public bool AutoSwitchModelsOnError { get; set; } = true;
        public bool AllowByoSessionExtension { get; set; } = false;
        public bool AllowFreeTrialSessionExtension { get; set; } = false;
        public bool PreferByoCreditsFirst { get; set; } = false;
        public bool AutoPauseOnInactivityEnabled { get; set; } = true;
        public int AutoPauseOnInactivityMinutes { get; set; } = 10;
        
        // Rotation state (persisted)
        public APIRotationState RotationState { get; set; } = new APIRotationState();
        public Dictionary<string, DateTime> ProviderModelCatalogRefreshedAtUtc { get; set; } = new Dictionary<string, DateTime>();
        
        // UI Settings
        public bool VoiceInputEnabled { get; set; } = true;
        public bool AutoSendAfterVoiceStopEnabled { get; set; } = true;
        public bool MicrophonePermissionGranted { get; set; } = false;
        public double WindowOpacity { get; set; } = 0.85;
        public bool UseFakeCursor { get; set; } = true;
        public double FakeCursorSize { get; set; } = 1.0;
        public string LegacyFallbackAppPath { get; set; } = "";
        
        // AI Configuration
        public string InterviewPromptType { get; set; } = InterviewPromptRegistry.InterviewTypes.Technical;
        public string SystemPrompt { get; set; } = InterviewPromptRegistry.ResolveSystemPrompt(InterviewPromptRegistry.InterviewTypes.Technical);
        public ManagedAiCatalogDto ManagedAiCatalogCache { get; set; } = new ManagedAiCatalogDto();
        public List<string> PremiumConfiguredProviders { get; set; } = new List<string>();
        public string SelectedHostedContextPackId { get; set; } = string.Empty;
        
        // User Data
        public string Resume { get; set; } = "";
        public string ResumeSummary { get; set; } = "";
        public string JobDescription { get; set; } = "";
        public string JobDescriptionSummary { get; set; } = "";

        // Debug Settings
        public bool DebugModeEnabled { get; set; } = false;
        public string DebugErrorSimulation { get; set; } = "None"; // "None", "429", "Timeout", "Random"

        // Failed Keys Tracking
        public List<int> ChatGPT_Failed { get; set; } = new List<int>();
        public List<int> Claude_Failed { get; set; } = new List<int>();
        public List<int> Mistral_Failed { get; set; } = new List<int>();
        public List<int> Gemini_Failed { get; set; } = new List<int>();
        public List<int> Groq_Failed { get; set; } = new List<int>();
        public List<int> NVIDIA_Failed { get; set; } = new List<int>();

        // ✅ REMOVED: CachedConversation and LastConversationSaved
        // These are now ONLY in conversation_cache.json (separate file)
    }

    // ═══════════════════════════════════════════════════════════════
    // API Rotation State (persisted across restarts)
    // ═══════════════════════════════════════════════════════════════
    public class APIRotationState
    {
        // Last used key index for each provider (round-robin)
        public Dictionary<string, int> LastKeyIndex { get; set; } = new Dictionary<string, int>();
        
        // Last used model index for each provider
        public Dictionary<string, int> LastModelIndex { get; set; } = new Dictionary<string, int>();
        
        // Keys that threw 429 errors (don't use immediately)
        public Dictionary<string, List<string>> FailedKeys429 { get; set; } = new Dictionary<string, List<string>>();
        
        // Timestamp of last 429 error per provider (for logging)
        public Dictionary<string, DateTime> Last429Time { get; set; } = new Dictionary<string, DateTime>();
    }

    // ═══════════════════════════════════════════════════════════════
    // Conversation Cache (stored in separate file)
    // ═══════════════════════════════════════════════════════════════
    public class ConversationCache
    {
        public List<ConversationMessage> Messages { get; set; } = new List<ConversationMessage>();
        public DateTime SavedAt { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════
    // Settings Manager - Handles all file operations
    // ═══════════════════════════════════════════════════════════════
    public static class SettingsManager
    {
        private static readonly object SyncLock = new object();
        private static readonly StorageBootstrapResult Bootstrap = StorageBootstrapper.Initialize();
        private static readonly SqliteRuntimeStore Store = new SqliteRuntimeStore(WindowsAppPaths.DatabasePath);
        private static readonly ISettingsRepository SettingsRepository = new SqliteSettingsRepository(Store);
        private static readonly IConversationCacheRepository ConversationRepository = new SqliteConversationCacheRepository(Store);
        private static readonly ISecretVault SecretVault = new WindowsSecretVault(Store);

        // ═══════════════════════════════════════════════════════════════
        // LOAD SETTINGS (from settings.json)
        // ═══════════════════════════════════════════════════════════════
        public static AppSettings Load()
        {
            try
            {
                if (!Bootstrap.IsReadOnlySafeMode)
                {
                    lock (SyncLock)
                    {
                        var settings = SettingsRepository.Load() ?? new AppSettings();
                        HydrateSecrets(settings);
                        PrepareSettings(settings, persistChanges: true);
                        Log.WriteLine($"✓ Settings loaded successfully from: {WindowsAppPaths.DatabasePath}");
                        return settings;
                    }
                }

                Log.WriteLine($"Storage bootstrap entered read-only safe mode: {Bootstrap.SafeModeReason}");
                var safeModeSettings = LoadLegacySettingsReadOnly() ?? new AppSettings();
                PrepareSettings(safeModeSettings, persistChanges: false);
                return safeModeSettings;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error loading settings: {ex.Message}");
                var fallback = new AppSettings();
                PrepareSettings(fallback, persistChanges: false);
                return fallback;
            }
        }


        // ═══════════════════════════════════════════════════════════════
        // CLEANUP DUPLICATE MODELS (fixes bug where models get duplicated)
        // ═══════════════════════════════════════════════════════════════
        private static void CleanupDuplicateModels(AppSettings settings)
        {
            bool hadDuplicates = false;

            // Remove duplicates from ChatGPT models
            var originalChatGPTCount = settings.ChatGPTModels.Count;
            settings.ChatGPTModels = settings.ChatGPTModels.Distinct().ToList();
            if (settings.ChatGPTModels.Count != originalChatGPTCount)
            {
                Log.WriteLine($"  Cleaned ChatGPT models: {originalChatGPTCount} → {settings.ChatGPTModels.Count}");
                hadDuplicates = true;
            }

            // Remove duplicates from Claude models
            var originalClaudeCount = settings.ClaudeModels.Count;
            settings.ClaudeModels = settings.ClaudeModels.Distinct().ToList();
            if (settings.ClaudeModels.Count != originalClaudeCount)
            {
                Log.WriteLine($"  Cleaned Claude models: {originalClaudeCount} → {settings.ClaudeModels.Count}");
                hadDuplicates = true;
            }

            // Remove duplicates from Mistral models
            var originalMistralCount = settings.MistralModels.Count;
            settings.MistralModels = settings.MistralModels.Distinct().ToList();
            if (settings.MistralModels.Count != originalMistralCount)
            {
                Log.WriteLine($"  Cleaned Mistral models: {originalMistralCount} → {settings.MistralModels.Count}");
                hadDuplicates = true;
            }

            // Remove duplicates from Gemini models
            var originalGeminiCount = settings.GeminiModels.Count;
            settings.GeminiModels = settings.GeminiModels.Distinct().ToList();
            if (settings.GeminiModels.Count != originalGeminiCount)
            {
                Log.WriteLine($"  Cleaned Gemini models: {originalGeminiCount} → {settings.GeminiModels.Count}");
                hadDuplicates = true;
            }

            // Remove duplicates from Groq models
            var originalGroqCount = settings.GroqModels.Count;
            settings.GroqModels = settings.GroqModels.Distinct().ToList();
            if (settings.GroqModels.Count != originalGroqCount)
            {
                Log.WriteLine($"  Cleaned Groq models: {originalGroqCount} → {settings.GroqModels.Count}");
                hadDuplicates = true;
            }

            var originalNvidiaCount = settings.NvidiaModels.Count;
            settings.NvidiaModels = settings.NvidiaModels.Distinct().ToList();
            if (settings.NvidiaModels.Count != originalNvidiaCount)
            {
                Log.WriteLine($"  Cleaned NVIDIA models: {originalNvidiaCount} → {settings.NvidiaModels.Count}");
                hadDuplicates = true;
            }

            // If we found duplicates, save the cleaned settings
            if (hadDuplicates)
            {
                Log.WriteLine("✓ Duplicate models removed - saving cleaned settings");
                Save(settings);
            }
        }


        // ═══════════════════════════════════════════════════════════════
        // SAVE SETTINGS (to settings.json)
        // Conversation is NOT saved here - use SaveConversationCache instead
        // ═══════════════════════════════════════════════════════════════
        public static void Save(AppSettings settings)
        {
            if (Bootstrap.IsReadOnlySafeMode)
            {
                Log.WriteLine("Storage is in read-only safe mode - skipping settings save");
                return;
            }

            try
            {
                lock (SyncLock)
                {
                    ApplyDerivedSettings(settings);
                    SecretVault.SaveProviderKeys(ExtractProviderKeys(settings));
                    var sanitizedSettings = CloneSettingsWithoutSecrets(settings);
                    SettingsRepository.Save(sanitizedSettings);
                    Log.WriteLine($"✓ Settings saved to SQLite: {WindowsAppPaths.DatabasePath}");
                }                
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // LOAD CONVERSATION CACHE (from conversation_cache.json)
        // ═══════════════════════════════════════════════════════════════
        public static ConversationCache? LoadConversationCache()
        {
            if (Bootstrap.IsReadOnlySafeMode)
            {
                return LoadLegacyConversationCacheReadOnly();
            }

            try
            {
                lock (SyncLock)
                {
                    var cache = ConversationRepository.Load();
                    if (cache != null && cache.Messages != null)
                    {
                        Log.WriteLine($"✓ Loaded {cache.Messages.Count} cached messages from {cache.SavedAt}");
                        return cache;
                    }
                }

                Log.WriteLine("No conversation cache entry found");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Warning: Failed to load conversation cache: {ex.Message}");
            }
            
            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        // SAVE CONVERSATION CACHE (to conversation_cache.json)
        // Only called by Restart button - NOT on normal close
        // ═══════════════════════════════════════════════════════════════
        public static void SaveConversationCache(List<ConversationMessage> messages)
        {
            if (Bootstrap.IsReadOnlySafeMode)
            {
                Log.WriteLine("Storage is in read-only safe mode - skipping conversation cache save");
                return;
            }

            try
            {
                var cache = new ConversationCache
                {
                    Messages = messages,
                    SavedAt = DateTime.Now
                };

                lock (SyncLock)
                {
                    ConversationRepository.Save(cache);
                }

                Log.WriteLine($"✓ Saved {messages.Count} messages to SQLite: {WindowsAppPaths.DatabasePath}");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error saving conversation cache: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // CLEAR CONVERSATION CACHE (delete conversation_cache.json)
        // Called on normal app close and Clear Chat button
        // ═══════════════════════════════════════════════════════════════
        public static void ClearConversationCache()
        {
            if (Bootstrap.IsReadOnlySafeMode)
            {
                Log.WriteLine("Storage is in read-only safe mode - skipping conversation cache clear");
                return;
            }

            try
            {
                lock (SyncLock)
                {
                    ConversationRepository.Clear();
                }

                Log.WriteLine($"✓ Conversation cache deleted from SQLite: {WindowsAppPaths.DatabasePath}");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error clearing conversation cache: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // MIGRATE LEGACY KEYS (backward compatibility)
        // ═══════════════════════════════════════════════════════════════
        private static void MigrateLegacyKeys(AppSettings settings)
        {
            bool migrated = false;

            // ChatGPT - only migrate if list is empty
            if (settings.ChatGPTApiKeys.Count == 0 && !string.IsNullOrWhiteSpace(settings.ChatGPTApiKey))
            {
                settings.ChatGPTApiKeys.Add(settings.ChatGPTApiKey);
                Log.WriteLine("  ✓ Migrated legacy ChatGPT key");
                migrated = true;
            }
            
            // Claude - only migrate if list is empty
            if (settings.ClaudeApiKeys.Count == 0 && !string.IsNullOrWhiteSpace(settings.ClaudeApiKey))
            {
                settings.ClaudeApiKeys.Add(settings.ClaudeApiKey);
                Log.WriteLine("  ✓ Migrated legacy Claude key");
                migrated = true;
            }
            
            // Mistral - only migrate if list is empty
            if (settings.MistralApiKeys.Count == 0 && !string.IsNullOrWhiteSpace(settings.MistralApiKey))
            {
                settings.MistralApiKeys.Add(settings.MistralApiKey);
                Log.WriteLine("  ✓ Migrated legacy Mistral key");
                migrated = true;
            }
            
            // Gemini - only migrate if list is empty
            if (settings.GeminiApiKeys.Count == 0 && !string.IsNullOrWhiteSpace(settings.GeminiApiKey))
            {
                settings.GeminiApiKeys.Add(settings.GeminiApiKey);
                Log.WriteLine("  ✓ Migrated legacy Gemini key");
                migrated = true;
            }
            
            // Groq - only migrate if list is empty
            if (settings.GroqApiKeys.Count == 0 && !string.IsNullOrWhiteSpace(settings.GroqApiKey))
            {
                settings.GroqApiKeys.Add(settings.GroqApiKey);
                Log.WriteLine("  ✓ Migrated legacy Groq key");
                migrated = true;
            }

            if (settings.NvidiaApiKeys.Count == 0 && !string.IsNullOrWhiteSpace(settings.NvidiaApiKey))
            {
                settings.NvidiaApiKeys.Add(settings.NvidiaApiKey);
                Log.WriteLine("  ✓ Migrated legacy NVIDIA key");
                migrated = true;
            }

            if (migrated)
            {
                Log.WriteLine("═══════════════════════════════════════════════════════");
                Log.WriteLine("LEGACY KEY MIGRATION COMPLETED");
            }
            
            // Log what we loaded
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("LOADED API KEYS:");
            Log.WriteLine($"  ChatGPT: {settings.ChatGPTApiKeys.Count} keys");
            Log.WriteLine($"  Claude: {settings.ClaudeApiKeys.Count} keys");
            Log.WriteLine($"  Mistral: {settings.MistralApiKeys.Count} keys");
            Log.WriteLine($"  Gemini: {settings.GeminiApiKeys.Count} keys");
            Log.WriteLine($"  Groq: {settings.GroqApiKeys.Count} keys");
            Log.WriteLine($"  NVIDIA: {settings.NvidiaApiKeys.Count} keys");
            Log.WriteLine("═══════════════════════════════════════════════════════");
        }

        /// <summary>
        /// Sync model lists in settings with AIModelRegistry
        /// Ensures dropdown and rotation manager use the same models
        /// </summary>
        public static void SyncModelListsWithRegistry(AppSettings settings)
        {
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("SYNCING MODEL LISTS WITH REGISTRY");
            
            bool changed = false;
            
            // Sync ChatGPT models
            var chatGPTModels = AIModelRegistry.GetModelsForProvider(AIModelRegistry.Providers.ChatGPT).ToList();
            if (settings.ChatGPTModels.Count == 0)
            {
                settings.ChatGPTModels = chatGPTModels;
                Log.WriteLine($"✓ Updated ChatGPT models: {chatGPTModels.Count} models");
                changed = true;
            }
            
            // Sync Claude models
            var claudeModels = AIModelRegistry.GetModelsForProvider(AIModelRegistry.Providers.Claude).ToList();
            if (settings.ClaudeModels.Count == 0)
            {
                settings.ClaudeModels = claudeModels;
                Log.WriteLine($"✓ Updated Claude models: {claudeModels.Count} models");
                changed = true;
            }
            
            // Sync Mistral models
            var mistralModels = AIModelRegistry.GetModelsForProvider(AIModelRegistry.Providers.Mistral).ToList();
            if (settings.MistralModels.Count == 0)
            {
                settings.MistralModels = mistralModels;
                Log.WriteLine($"✓ Updated Mistral models: {mistralModels.Count} models");
                changed = true;
            }
            
            // Sync Gemini models
            var geminiModels = AIModelRegistry.GetModelsForProvider(AIModelRegistry.Providers.Gemini).ToList();
            if (settings.GeminiModels.Count == 0)
            {
                settings.GeminiModels = geminiModels;
                Log.WriteLine($"✓ Updated Gemini models: {geminiModels.Count} models");
                changed = true;
            }
            
            // Sync Groq models
            var groqModels = AIModelRegistry.GetModelsForProvider(AIModelRegistry.Providers.Groq).ToList();
            if (settings.GroqModels.Count == 0)
            {
                settings.GroqModels = groqModels;
                Log.WriteLine($"✓ Updated Groq models: {groqModels.Count} models");
                changed = true;
            }

            var nvidiaModels = AIModelRegistry.GetModelsForProvider(AIModelRegistry.Providers.Nvidia).ToList();
            if (settings.NvidiaModels.Count == 0 && nvidiaModels.Count > 0)
            {
                settings.NvidiaModels = nvidiaModels;
                Log.WriteLine($"✓ Updated NVIDIA models: {nvidiaModels.Count} models");
                changed = true;
            }
            
            if (!changed)
            {
                Log.WriteLine("✓ All model lists already in sync");
            }
            
            Log.WriteLine("═══════════════════════════════════════════════════════");
        }



        // ═══════════════════════════════════════════════════════════════
        // GET FILE PATHS (for debugging)
        // ═══════════════════════════════════════════════════════════════
        public static string GetSettingsPath() => WindowsAppPaths.DatabasePath;
        public static string GetConversationCachePath() => WindowsAppPaths.DatabasePath;
        public static bool IsReadOnlySafeMode() => Bootstrap.IsReadOnlySafeMode;
        public static string? GetSafeModeReason() => Bootstrap.SafeModeReason;

        private static void PrepareSettings(AppSettings settings, bool persistChanges)
        {
            MigrateLegacyKeys(settings);
            CleanupDuplicateModels(settings);
            SyncModelListsWithRegistry(settings);
            ProviderModelCatalogCache.BackfillFromLegacySettings(settings);
            ByoProviderModelCatalogService.RefreshStaleCatalogs(settings);
            ApplyDerivedSettings(settings);

            if (persistChanges)
            {
                Save(settings);
            }
        }

        private static void ApplyDerivedSettings(AppSettings settings)
        {
            settings.InterviewPromptType = NormalizeInterviewPromptType(settings.InterviewPromptType);
            settings.SystemPrompt = InterviewPromptRegistry.ResolveSystemPrompt(settings.InterviewPromptType);
            settings.AutoPauseOnInactivityMinutes = Math.Max(10, settings.AutoPauseOnInactivityMinutes);
            settings.PremiumConfiguredProviders = settings.PremiumConfiguredProviders
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            ProviderModelCatalogCache.SyncLegacyModelListsFromCache(settings);
        }

        private static string NormalizeInterviewPromptType(string? interviewPromptType)
        {
            if (string.IsNullOrWhiteSpace(interviewPromptType))
            {
                return InterviewPromptRegistry.InterviewTypes.Technical;
            }

            return InterviewPromptRegistry.GetAllInterviewTypes()
                .FirstOrDefault(item => string.Equals(item, interviewPromptType, StringComparison.OrdinalIgnoreCase))
                ?? InterviewPromptRegistry.InterviewTypes.Technical;
        }

        private static AppSettings? LoadLegacySettingsReadOnly()
        {
            try
            {
                if (!File.Exists(WindowsAppPaths.LegacySettingsPath))
                {
                    return null;
                }

                var json = File.ReadAllText(WindowsAppPaths.LegacySettingsPath);
                return JsonConvert.DeserializeObject<AppSettings>(json);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Failed to read legacy settings in safe mode: {ex.Message}");
                return null;
            }
        }

        private static ConversationCache? LoadLegacyConversationCacheReadOnly()
        {
            try
            {
                if (!File.Exists(WindowsAppPaths.LegacyConversationCachePath))
                {
                    return null;
                }

                var cacheJson = File.ReadAllText(WindowsAppPaths.LegacyConversationCachePath);
                var cache = JsonConvert.DeserializeObject<ConversationCache>(cacheJson);
                if (cache != null && cache.Messages != null)
                {
                    Log.WriteLine($"✓ Loaded {cache.Messages.Count} legacy cached messages in safe mode");
                }

                return cache;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Failed to read legacy conversation cache in safe mode: {ex.Message}");
                return null;
            }
        }

        private static void HydrateSecrets(AppSettings settings)
        {
            var providerKeys = SecretVault.LoadProviderKeys();
            if (providerKeys.Count == 0)
            {
                return;
            }

            settings.ChatGPTApiKeys = providerKeys.TryGetValue("ChatGPT", out var chatGptKeys) ? chatGptKeys : new List<string>();
            settings.ClaudeApiKeys = providerKeys.TryGetValue("Claude", out var claudeKeys) ? claudeKeys : new List<string>();
            settings.MistralApiKeys = providerKeys.TryGetValue("Mistral", out var mistralKeys) ? mistralKeys : new List<string>();
            settings.GeminiApiKeys = providerKeys.TryGetValue("Gemini", out var geminiKeys) ? geminiKeys : new List<string>();
            settings.GroqApiKeys = providerKeys.TryGetValue("Groq", out var groqKeys) ? groqKeys : new List<string>();
            settings.NvidiaApiKeys = providerKeys.TryGetValue("NVIDIA", out var nvidiaKeys) ? nvidiaKeys : new List<string>();

            settings.ChatGPTApiKey = settings.ChatGPTApiKeys.FirstOrDefault() ?? "";
            settings.ClaudeApiKey = settings.ClaudeApiKeys.FirstOrDefault() ?? "";
            settings.MistralApiKey = settings.MistralApiKeys.FirstOrDefault() ?? "";
            settings.GeminiApiKey = settings.GeminiApiKeys.FirstOrDefault() ?? "";
            settings.GroqApiKey = settings.GroqApiKeys.FirstOrDefault() ?? "";
            settings.NvidiaApiKey = settings.NvidiaApiKeys.FirstOrDefault() ?? "";
        }

        private static Dictionary<string, List<string>> ExtractProviderKeys(AppSettings settings)
        {
            return new Dictionary<string, List<string>>
            {
                ["ChatGPT"] = settings.ChatGPTApiKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToList(),
                ["Claude"] = settings.ClaudeApiKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToList(),
                ["Mistral"] = settings.MistralApiKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToList(),
                ["Gemini"] = settings.GeminiApiKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToList(),
                ["Groq"] = settings.GroqApiKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToList(),
                ["NVIDIA"] = settings.NvidiaApiKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToList()
            };
        }

        private static AppSettings CloneSettingsWithoutSecrets(AppSettings source)
        {
            var clone = JsonConvert.DeserializeObject<AppSettings>(
                JsonConvert.SerializeObject(source, Formatting.Indented)) ?? new AppSettings();

            clone.ChatGPTApiKeys = new List<string>();
            clone.ClaudeApiKeys = new List<string>();
            clone.MistralApiKeys = new List<string>();
            clone.GeminiApiKeys = new List<string>();
            clone.GroqApiKeys = new List<string>();
            clone.NvidiaApiKeys = new List<string>();

            clone.ChatGPTApiKey = "";
            clone.ClaudeApiKey = "";
            clone.MistralApiKey = "";
            clone.GeminiApiKey = "";
            clone.GroqApiKey = "";
            clone.NvidiaApiKey = "";

            return clone;
        }
    }
}
