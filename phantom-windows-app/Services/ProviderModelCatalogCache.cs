using System;
using System.Collections.Generic;
using System.Linq;
using SecureOverlay.Helpers;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Services
{
    public static class ProviderModelCatalogCache
    {
        public static ManagedAiProviderOptionDto? GetProvider(AppSettings settings, string providerId, bool byo = false)
        {
            return GetCatalog(settings, byo).Providers?
                .FirstOrDefault(item => string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        }

        public static string[] GetModelIds(AppSettings settings, string providerId, bool byo = false)
        {
            return (GetProvider(settings, providerId, byo)?.Models ?? new List<ManagedAiModelOptionDto>())
                .Select(item => item.ModelId)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static ManagedAiModelOptionDto? GetModel(AppSettings settings, string providerId, string modelId, bool byo = false)
        {
            return GetProvider(settings, providerId, byo)?.Models?
                .FirstOrDefault(item => string.Equals(item.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
        }

        public static void MergeCatalog(AppSettings settings, ManagedAiCatalogDto catalog)
        {
            settings.ManagedAiCatalogCache ??= new ManagedAiCatalogDto();
            settings.ManagedAiCatalogCache.Providers ??= new List<ManagedAiProviderOptionDto>();

            foreach (var provider in catalog.Providers ?? new List<ManagedAiProviderOptionDto>())
            {
                UpsertProvider(settings, provider.ProviderId, provider.Label, provider.Models, provider.RefreshedAtUtc);
            }

            settings.ManagedAiCatalogCache.RefreshedAtUtc = settings.ManagedAiCatalogCache.Providers.Count == 0
                ? catalog.RefreshedAtUtc
                : settings.ManagedAiCatalogCache.Providers.Max(item => item.RefreshedAtUtc);

        }

        public static void ReplaceCatalog(AppSettings settings, ManagedAiCatalogDto catalog)
        {
            settings.ManagedAiCatalogCache = new ManagedAiCatalogDto
            {
                RefreshedAtUtc = catalog.RefreshedAtUtc,
                Providers = (catalog.Providers ?? new List<ManagedAiProviderOptionDto>())
                    .Where(item => !string.IsNullOrWhiteSpace(item.ProviderId))
                    .Select(item => new ManagedAiProviderOptionDto
                    {
                        ProviderId = item.ProviderId,
                        Label = string.IsNullOrWhiteSpace(item.Label) ? item.ProviderId : item.Label,
                        RefreshedAtUtc = item.RefreshedAtUtc,
                        Models = (item.Models ?? new List<ManagedAiModelOptionDto>())
                            .Where(model => !string.IsNullOrWhiteSpace(model.ModelId))
                            .GroupBy(model => model.ModelId, StringComparer.OrdinalIgnoreCase)
                            .Select(group =>
                            {
                                var first = group.First();
                                return new ManagedAiModelOptionDto
                                {
                                    ModelId = first.ModelId,
                                    DisplayName = string.IsNullOrWhiteSpace(first.DisplayName) ? first.ModelId : first.DisplayName,
                                    SupportsVision = group.Any(model => model.SupportsVision)
                                };
                            })
                            .ToList()
                    })
                    .ToList()
            };

        }

        public static void UpsertProvider(
            AppSettings settings,
            string providerId,
            string? label,
            IEnumerable<ManagedAiModelOptionDto> models,
            DateTime refreshedAtUtc,
            bool byo = false)
        {
            var catalog = GetCatalog(settings, byo);
            catalog.Providers ??= new List<ManagedAiProviderOptionDto>();

            var normalizedModels = models
                .Where(item => !string.IsNullOrWhiteSpace(item.ModelId))
                .GroupBy(item => item.ModelId, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var first = group.First();
                    return new ManagedAiModelOptionDto
                    {
                        ModelId = first.ModelId,
                        DisplayName = string.IsNullOrWhiteSpace(first.DisplayName) ? first.ModelId : first.DisplayName,
                        SupportsVision = group.Any(item => item.SupportsVision)
                    };
                })
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var existing = GetProvider(settings, providerId, byo);
            if (existing == null)
            {
                catalog.Providers.Add(new ManagedAiProviderOptionDto
                {
                    ProviderId = providerId,
                    Label = string.IsNullOrWhiteSpace(label) ? providerId : label!,
                    Models = normalizedModels,
                    RefreshedAtUtc = refreshedAtUtc
                });
            }
            else
            {
                existing.Label = string.IsNullOrWhiteSpace(label) ? existing.Label : label!;
                existing.Models = normalizedModels;
                existing.RefreshedAtUtc = refreshedAtUtc;
            }

            catalog.Providers = catalog.Providers
                .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            catalog.RefreshedAtUtc = catalog.Providers.Count == 0
                ? refreshedAtUtc
                : catalog.Providers.Max(item => item.RefreshedAtUtc);

            if (byo)
            {
                SyncLegacyModelsForProvider(settings, providerId, normalizedModels.Select(item => item.ModelId));
            }
        }

        public static void SyncLegacyModelListsFromCache(AppSettings settings, bool byo)
        {
            foreach (var providerId in AIModelRegistry.GetAllProviders())
            {
                SyncLegacyModelsForProvider(settings, providerId, GetModelIds(settings, providerId, byo));
            }
        }

        public static bool InferVisionSupport(string? modelId, string? displayName)
        {
            if (!string.IsNullOrWhiteSpace(modelId) && AIModelRegistry.SupportsVision(modelId))
            {
                return true;
            }

            var normalized = $"{modelId} {displayName}".ToLowerInvariant();
            return normalized.Contains("vision")
                || normalized.Contains("4o")
                || normalized.Contains("omni")
                || normalized.Contains("claude")
                || normalized.Contains("gemini")
                || normalized.Contains("mistral-large")
                || normalized.Contains("pixtral")
                || normalized.Contains("vlm")
                || normalized.Contains("image")
                || normalized.Contains("multimodal")
                || normalized.Contains("llama-4")
                || normalized.Contains("scout");
        }

        private static ManagedAiCatalogDto GetCatalog(AppSettings settings, bool byo)
        {
            if (byo)
            {
                settings.ByoAiCatalogCache ??= new ManagedAiCatalogDto();
                return settings.ByoAiCatalogCache;
            }

            settings.ManagedAiCatalogCache ??= new ManagedAiCatalogDto();
            return settings.ManagedAiCatalogCache;
        }

        private static void SyncLegacyModelsForProvider(AppSettings settings, string providerId, IEnumerable<string> models)
        {
            var normalized = models
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            switch (providerId)
            {
                case "ChatGPT":
                    settings.ChatGPTModels = normalized;
                    break;
                case "Claude":
                    settings.ClaudeModels = normalized;
                    break;
                case "Mistral":
                    settings.MistralModels = normalized;
                    break;
                case "Gemini":
                    settings.GeminiModels = normalized;
                    break;
                case "Groq":
                    settings.GroqModels = normalized;
                    break;
                case "NVIDIA":
                    settings.NvidiaModels = normalized;
                    break;
                case "OpenRouter":
                    settings.OpenRouterModels = normalized;
                    break;
            }
        }
    }
}
