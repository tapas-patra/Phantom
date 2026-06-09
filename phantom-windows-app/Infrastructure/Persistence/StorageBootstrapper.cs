using System;
using System.IO;
using Newtonsoft.Json;
using SecureOverlay.Domain.ValueObjects;
using SecureOverlay.Platform.Windows;
using SecureOverlay.Services;

namespace SecureOverlay.Infrastructure.Persistence
{
    public static class StorageBootstrapper
    {
        private static readonly Lazy<StorageBootstrapResult> _bootstrap = new Lazy<StorageBootstrapResult>(InitializeInternal);

        public static StorageBootstrapResult Initialize() => _bootstrap.Value;

        private static StorageBootstrapResult InitializeInternal()
        {
            try
            {
                Directory.CreateDirectory(WindowsAppPaths.PrimaryRoot);
                Directory.CreateDirectory(WindowsAppPaths.TempRoot);
                Directory.CreateDirectory(WindowsAppPaths.WebView2CachePath);

                var store = new SqliteRuntimeStore(WindowsAppPaths.DatabasePath);
                store.EnsureSchema();
                MigrateLegacyStateIfNeeded(store);

                ClearSafeModeMarkerIfPresent();
                return new StorageBootstrapResult(StorageMode.ReadWrite, WindowsAppPaths.DatabasePath);
            }
            catch (Exception ex)
            {
                EnterSafeMode(ex);
                return new StorageBootstrapResult(
                    StorageMode.ReadOnlySafeMode,
                    WindowsAppPaths.DatabasePath,
                    ex.Message);
            }
        }

        private static void MigrateLegacyStateIfNeeded(SqliteRuntimeStore store)
        {
            if (!store.HasEntry(SqliteRuntimeStore.SettingsKey) && File.Exists(WindowsAppPaths.LegacySettingsPath))
            {
                var settingsJson = File.ReadAllText(WindowsAppPaths.LegacySettingsPath);
                _ = JsonConvert.DeserializeObject<AppSettings>(settingsJson)
                    ?? throw new InvalidDataException("Legacy settings.json could not be deserialized.");
                store.UpsertPayload(SqliteRuntimeStore.SettingsKey, settingsJson, DateTime.UtcNow);
            }

            if (!store.HasEntry(SqliteRuntimeStore.ConversationCacheKey) && File.Exists(WindowsAppPaths.LegacyConversationCachePath))
            {
                var cacheJson = File.ReadAllText(WindowsAppPaths.LegacyConversationCachePath);
                _ = JsonConvert.DeserializeObject<ConversationCache>(cacheJson)
                    ?? throw new InvalidDataException("Legacy conversation_cache.json could not be deserialized.");
                store.UpsertPayload(SqliteRuntimeStore.ConversationCacheKey, cacheJson, DateTime.UtcNow);
            }
        }

        private static void EnterSafeMode(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(WindowsAppPaths.PrimaryRoot);
                File.WriteAllText(
                    WindowsAppPaths.SafeModeMarkerPath,
                    $"[{DateTime.UtcNow:O}] Storage bootstrap failed. Entered read-only safe mode.{Environment.NewLine}{ex}");
            }
            catch
            {
                // Safe-mode marker is best effort only.
            }
        }

        private static void ClearSafeModeMarkerIfPresent()
        {
            try
            {
                if (File.Exists(WindowsAppPaths.SafeModeMarkerPath))
                {
                    File.Delete(WindowsAppPaths.SafeModeMarkerPath);
                }
            }
            catch
            {
                // Best effort only.
            }
        }
    }
}
