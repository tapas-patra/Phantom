using System;
using SecureOverlay.Services;

namespace SecureOverlay.Helpers
{
    public static class CatalogRefreshQuota
    {
        public const int MaxAutomaticRefreshesPerLocalDay = 2;

        public static bool IsChatCatalogEmpty(AppSettings settings)
        {
            var byoEmpty = (settings.ByoAiCatalogCache?.Providers?.Count ?? 0) == 0;
            var managedEmpty = (settings.ManagedAiCatalogCache?.Providers?.Count ?? 0) == 0;
            return byoEmpty && managedEmpty;
        }

        public static bool IsSpeechCatalogEmpty(AppSettings settings)
        {
            return (settings.SpeechCatalogCache?.Providers?.Count ?? 0) == 0;
        }

        /// <summary>
        /// Returns true when an automatic catalog refresh should run.
        /// Cold start with empty cache always fetches once. Explicit refreshes should pass force: true and skip this gate.
        /// </summary>
        public static bool TryConsumeAutomaticRefresh(AppSettings settings, bool cacheIsEmpty)
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            if (!string.Equals(settings.CatalogAutoRefreshLocalDate, today, StringComparison.Ordinal))
            {
                settings.CatalogAutoRefreshLocalDate = today;
                settings.CatalogAutoRefreshCountForLocalDate = 0;
            }

            if (cacheIsEmpty && settings.CatalogAutoRefreshCountForLocalDate == 0)
            {
                settings.CatalogAutoRefreshCountForLocalDate = 1;
                return true;
            }

            if (settings.CatalogAutoRefreshCountForLocalDate >= MaxAutomaticRefreshesPerLocalDay)
            {
                return false;
            }

            settings.CatalogAutoRefreshCountForLocalDate++;
            return true;
        }
    }
}
