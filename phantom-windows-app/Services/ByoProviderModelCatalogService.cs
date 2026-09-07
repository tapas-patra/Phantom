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

        public static async Task RefreshStaleCatalogsAsync(
            AppSettings settings,
            IHostedAccountClient hostedClient,
            string accessToken,
            string? forceProvider = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var catalog = await hostedClient.GetByoCatalogAsync(accessToken, cancellationToken);
                var cachedProviders = settings.ByoAiCatalogCache?.Providers ?? new List<ManagedAiProviderOptionDto>();
                settings.ByoAiCatalogCache = new ManagedAiCatalogDto
                {
                    RefreshedAtUtc = catalog.RefreshedAtUtc,
                    Providers = (catalog.Providers ?? new List<ManagedAiProviderOptionDto>())
                        .Select(provider =>
                        {
                            var cached = cachedProviders.FirstOrDefault(item => string.Equals(
                                item.ProviderId,
                                provider.ProviderId,
                                StringComparison.OrdinalIgnoreCase));
                            return new ManagedAiProviderOptionDto
                            {
                                ProviderId = provider.ProviderId,
                                Label = provider.Label,
                                Models = cached?.Models ?? new List<ManagedAiModelOptionDto>(),
                                RefreshedAtUtc = cached?.RefreshedAtUtc ?? DateTime.MinValue
                            };
                        })
                        .ToList()
                };
                ProviderModelCatalogCache.SyncLegacyModelListsFromCache(settings, byo: true);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"BYO provider catalog refresh skipped: {ex.GetType().Name}");
            }

            var providers = settings.ByoAiCatalogCache?.Providers?.ToArray()
                ?? Array.Empty<ManagedAiProviderOptionDto>();
            foreach (var provider in providers)
            {
                var keys = GetKeysForProvider(settings, provider.ProviderId);
                if (keys.Count == 0)
                {
                    continue;
                }

                var forced = string.Equals(forceProvider, provider.ProviderId, StringComparison.OrdinalIgnoreCase);
                var stale = provider.Models.Count == 0 || provider.RefreshedAtUtc <= DateTime.UtcNow - RefreshInterval;
                if (!forced && !stale)
                {
                    continue;
                }

                try
                {
                    var refreshed = await hostedClient.RefreshByoProviderCatalogAsync(
                        accessToken,
                        new ByoModelCatalogRequestDto { ProviderId = provider.ProviderId, ApiKey = keys[0] },
                        cancellationToken);
                    if (refreshed.Models.Count == 0)
                    {
                        Log.WriteLine($"BYO model refresh returned no chat models for {provider.ProviderId}");
                        continue;
                    }

                    ProviderModelCatalogCache.UpsertProvider(
                        settings,
                        refreshed.ProviderId,
                        refreshed.Label,
                        refreshed.Models,
                        refreshed.RefreshedAtUtc,
                        byo: true);
                    settings.ProviderModelCatalogRefreshedAtUtc[provider.ProviderId] = refreshed.RefreshedAtUtc;

                    var currentModel = AIModelRegistry.GetCurrentModelForProvider(settings, provider.ProviderId);
                    if (!refreshed.Models.Any(item => string.Equals(item.ModelId, currentModel, StringComparison.OrdinalIgnoreCase)))
                    {
                        AIModelRegistry.SetModelForProvider(settings, provider.ProviderId, refreshed.Models[0].ModelId);
                    }

                    Log.WriteLine($"BYO catalog refreshed through backend: provider={provider.ProviderId}, models={refreshed.Models.Count}");
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"BYO model refresh failed for {provider.ProviderId}: {ex.GetType().Name}");
                }
            }
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
    }
}
