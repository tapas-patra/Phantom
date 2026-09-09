using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SecureOverlay.Helpers;
using SecureOverlay.Infrastructure.Hosted;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Services
{
    public static class ByoProviderModelCatalogService
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(12);

        public static async Task<bool> RefreshStaleCatalogsAsync(
            AppSettings settings,
            IHostedAccountClient hostedClient,
            string accessToken,
            string? forceProvider = null,
            bool forceAll = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var needsForceRefresh = forceAll
                    || !string.IsNullOrWhiteSpace(forceProvider)
                    || IsCatalogStale(settings);

                var catalog = needsForceRefresh
                    ? await hostedClient.RefreshByoCatalogAsync(
                        accessToken,
                        new ByoModelCatalogRequestDto
                        {
                            ProviderId = forceAll ? string.Empty : (forceProvider ?? string.Empty)
                        },
                        cancellationToken)
                    : await hostedClient.GetByoCatalogAsync(accessToken, cancellationToken);

                ApplyCatalog(settings, catalog);
                SettingsManager.Save(settings);
                return true;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"BYO provider catalog refresh skipped: {ex.GetType().Name}");
                // Keep previous cache; never seed hardcoded registry models on failure.
                return false;
            }
        }

        private static bool IsCatalogStale(AppSettings settings)
        {
            var providers = settings.ByoAiCatalogCache?.Providers;
            // Missing cache entirely is stale. Empty models after a successful fetch are valid.
            if (providers == null || providers.Count == 0)
            {
                return true;
            }

            var refreshedAt = settings.ByoAiCatalogCache?.RefreshedAtUtc ?? DateTime.MinValue;
            return refreshedAt <= DateTime.UtcNow - RefreshInterval;
        }

        private static void ApplyCatalog(AppSettings settings, ManagedAiCatalogDto catalog)
        {
            // Trust backend providers/models as-is (empty lists allowed). No registry model seeding.
            var providers = (catalog.Providers ?? new List<ManagedAiProviderOptionDto>())
                .Where(item => !string.IsNullOrWhiteSpace(item.ProviderId))
                .Select(item => new ManagedAiProviderOptionDto
                {
                    ProviderId = item.ProviderId,
                    Label = string.IsNullOrWhiteSpace(item.Label) ? item.ProviderId : item.Label,
                    Models = item.Models ?? new List<ManagedAiModelOptionDto>(),
                    RefreshedAtUtc = item.RefreshedAtUtc
                })
                .ToList();

            settings.ByoAiCatalogCache = new ManagedAiCatalogDto
            {
                RefreshedAtUtc = catalog.RefreshedAtUtc == default ? DateTime.UtcNow : catalog.RefreshedAtUtc,
                Providers = providers
            };

            ProviderModelCatalogCache.SyncLegacyModelListsFromCache(settings, byo: true);

            foreach (var provider in providers)
            {
                settings.ProviderModelCatalogRefreshedAtUtc[provider.ProviderId] = provider.RefreshedAtUtc;
            }
        }
    }
}
