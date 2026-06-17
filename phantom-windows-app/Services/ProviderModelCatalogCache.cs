using System;
using System.Collections.Generic;
using System.Linq;
using SecureOverlay.Helpers;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Services
{
    public static class ProviderModelCatalogCache
    {
        public static ManagedAiProviderOptionDto? GetProvider(AppSettings settings, string providerId)
        {
            return settings.ManagedAiCatalogCache?.Providers?
                .FirstOrDefault(item => string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        }

        public static string[] GetModelIds(AppSettings settings, string providerId)
        {
            var cachedModels = GetProvider(settings, providerId)?.Models?
                .Select(item => item.ModelId)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (cachedModels != null && cachedModels.Length > 0)
            {
                return cachedModels;
            }

            return GetLegacyModelList(settings, providerId)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static ManagedAiModelOptionDto? GetModel(AppSettings settings, string providerId, string modelId)
        {
            return GetProvider(settings, providerId)?.Models?
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

            SyncLegacyModelListsFromCache(settings);
        }

        public static void UpsertProvider(
            AppSettings settings,
            string providerId,
            string? label,
            IEnumerable<ManagedAiModelOptionDto> models,
            DateTime refreshedAtUtc)
        {
            settings.ManagedAiCatalogCache ??= new ManagedAiCatalogDto();
            settings.ManagedAiCatalogCache.Providers ??= new List<ManagedAiProviderOptionDto>();

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

            var existing = GetProvider(settings, providerId);
            if (existing == null)
            {
                settings.ManagedAiCatalogCache.Providers.Add(new ManagedAiProviderOptionDto
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

            settings.ManagedAiCatalogCache.Providers = settings.ManagedAiCatalogCache.Providers
                .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            settings.ManagedAiCatalogCache.RefreshedAtUtc = settings.ManagedAiCatalogCache.Providers.Count == 0
                ? refreshedAtUtc
                : settings.ManagedAiCatalogCache.Providers.Max(item => item.RefreshedAtUtc);

            SyncLegacyModelsForProvider(settings, providerId, normalizedModels.Select(item => item.ModelId));
        }

        public static void BackfillFromLegacySettings(AppSettings settings)
        {
            foreach (var providerId in AIModelRegistry.GetAllProviders())
            {
                if (GetProvider(settings, providerId)?.Models?.Count > 0)
                {
                    continue;
                }

                var models = GetLegacyModelList(settings, providerId)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(modelId => new ManagedAiModelOptionDto
                    {
                        ModelId = modelId,
                        DisplayName = AIModelRegistry.GetDisplayName(modelId),
                        SupportsVision = InferVisionSupport(modelId, AIModelRegistry.GetDisplayName(modelId))
                    })
                    .ToList();

                if (models.Count == 0)
                {
                    continue;
                }

                UpsertProvider(settings, providerId, providerId, models, DateTime.UtcNow);
            }
        }

        public static void SyncLegacyModelListsFromCache(AppSettings settings)
        {
            foreach (var providerId in AIModelRegistry.GetAllProviders())
            {
                SyncLegacyModelsForProvider(settings, providerId, GetModelIds(settings, providerId));
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
                || normalized.Contains("pixtral")
                || normalized.Contains("vlm")
                || normalized.Contains("image")
                || normalized.Contains("multimodal")
                || normalized.Contains("llama-4")
                || normalized.Contains("scout");
        }

        private static List<string> GetLegacyModelList(AppSettings settings, string providerId)
        {
            return providerId switch
            {
                "ChatGPT" => settings.ChatGPTModels,
                "Claude" => settings.ClaudeModels,
                "Mistral" => settings.MistralModels,
                "Gemini" => settings.GeminiModels,
                "Groq" => settings.GroqModels,
                "NVIDIA" => settings.NvidiaModels,
                _ => AIModelRegistry.GetModelsForProvider(providerId).ToList()
            };
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
            }
        }
    }
}
