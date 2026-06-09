using System;
using System.Collections.Generic;
using System.Linq;
using SecureOverlay.Application.Context;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Services;

namespace SecureOverlay.Infrastructure.Context
{
    public sealed class LocalContextPackService : IContextPackService
    {
        private readonly IContextPackRepository _repository;

        public LocalContextPackService(IContextPackRepository repository)
        {
            _repository = repository;
        }

        public IReadOnlyList<ContextPackSummary> GetAllPacks()
        {
            var state = EnsureState();
            return state.Packs
                .OrderByDescending(pack => pack.PackId == state.SelectedPackId)
                .ThenBy(pack => pack.Name)
                .Select(pack => new ContextPackSummary
                {
                    PackId = pack.PackId,
                    Name = pack.Name,
                    IsSelected = pack.PackId == state.SelectedPackId,
                    DocumentCount = pack.Documents?.Count ?? 0,
                    UpdatedAtUtc = pack.UpdatedAtUtc
                })
                .ToList();
        }

        public ContextPack GetSelectedPack()
        {
            var state = EnsureState();

            var selectedPack = state.Packs.FirstOrDefault(pack => pack.PackId == state.SelectedPackId);
            if (selectedPack != null)
            {
                return selectedPack;
            }

            var fallbackPack = state.Packs[0];
            state.SelectedPackId = fallbackPack.PackId;
            _repository.Save(state);
            return fallbackPack;
        }

        public ContextPack CreatePack(string name)
        {
            var state = EnsureState();
            var trimmedName = string.IsNullOrWhiteSpace(name) ? "New Context Pack" : name.Trim();
            var pack = new ContextPack
            {
                PackId = $"pack-{Guid.NewGuid():N}",
                Name = trimmedName,
                UpdatedAtUtc = DateTime.UtcNow
            };

            pack.Documents = BuildDocuments(pack);
            state.Packs.Add(pack);
            state.SelectedPackId = pack.PackId;
            _repository.Save(state);
            return pack;
        }

        public void SelectPack(string packId)
        {
            var state = EnsureState();
            var exists = state.Packs.Any(pack => pack.PackId == packId);
            if (!exists)
            {
                return;
            }

            state.SelectedPackId = packId;
            _repository.Save(state);
        }

        public void SaveSelectedPack(ContextPack pack)
        {
            var state = EnsureState();
            var existingIndex = state.Packs.FindIndex(existing => existing.PackId == pack.PackId);
            pack.Documents = BuildDocuments(pack);
            pack.UpdatedAtUtc = DateTime.UtcNow;

            if (existingIndex >= 0)
            {
                state.Packs[existingIndex] = pack;
            }
            else
            {
                state.Packs.Add(pack);
            }

            state.SelectedPackId = pack.PackId;
            _repository.Save(state);
        }

        public void RenamePack(string packId, string name)
        {
            var state = EnsureState();
            var pack = state.Packs.FirstOrDefault(existing => existing.PackId == packId);
            if (pack == null)
            {
                return;
            }

            pack.Name = string.IsNullOrWhiteSpace(name) ? pack.Name : name.Trim();
            pack.UpdatedAtUtc = DateTime.UtcNow;
            _repository.Save(state);
        }

        public void DeletePack(string packId)
        {
            var state = EnsureState();
            if (state.Packs.Count <= 1)
            {
                return;
            }

            state.Packs.RemoveAll(pack => pack.PackId == packId);
            if (!state.Packs.Any(pack => pack.PackId == state.SelectedPackId))
            {
                state.SelectedPackId = state.Packs[0].PackId;
            }

            _repository.Save(state);
        }

        public void ClearSelectedPack(bool clearResume, bool clearJobDescription)
        {
            var pack = GetSelectedPack();
            if (clearResume)
            {
                pack.ResumeText = string.Empty;
                pack.ResumeSummary = string.Empty;
            }

            if (clearJobDescription)
            {
                pack.JobDescriptionText = string.Empty;
                pack.JobDescriptionSummary = string.Empty;
            }

            SaveSelectedPack(pack);
        }

        private ContextPackState EnsureState()
        {
            var state = _repository.Load();
            if (state != null && state.Packs.Count > 0)
            {
                return state;
            }

            var migrated = CreateLegacyMigratedPack();
            var newState = new ContextPackState
            {
                SelectedPackId = migrated.PackId,
                Packs = new List<ContextPack> { migrated }
            };
            _repository.Save(newState);
            return newState;
        }

        private static ContextPack CreateLegacyMigratedPack()
        {
            var settings = SettingsManager.Load();
            var pack = new ContextPack
            {
                PackId = "default",
                Name = "Default Context Pack",
                ResumeText = settings.Resume,
                ResumeSummary = settings.ResumeSummary,
                JobDescriptionText = settings.JobDescription,
                JobDescriptionSummary = settings.JobDescriptionSummary,
                UpdatedAtUtc = DateTime.UtcNow
            };
            pack.Documents = BuildDocuments(pack);
            return pack;
        }

        private static System.Collections.Generic.List<ContextDocument> BuildDocuments(ContextPack pack)
        {
            var documents = new System.Collections.Generic.List<ContextDocument>();

            if (!string.IsNullOrWhiteSpace(pack.ResumeText))
            {
                documents.Add(new ContextDocument
                {
                    DocumentId = "resume",
                    Title = "Resume Profile",
                    SourceType = "resume",
                    Body = pack.ResumeText,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Chunks = BuildChunks("resume", pack.ResumeText)
                });
            }

            if (!string.IsNullOrWhiteSpace(pack.JobDescriptionText))
            {
                documents.Add(new ContextDocument
                {
                    DocumentId = "job-description",
                    Title = "Job Description",
                    SourceType = "job_description",
                    Body = pack.JobDescriptionText,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Chunks = BuildChunks("job-description", pack.JobDescriptionText)
                });
            }

            return documents;
        }

        private static System.Collections.Generic.List<ContextChunk> BuildChunks(string documentId, string body)
        {
            var chunks = new System.Collections.Generic.List<ContextChunk>();
            var normalized = body.Replace("\r\n", "\n");
            var parts = normalized
                .Split(new[] { "\n\n", "\n", ". " }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToList();

            if (parts.Count == 0 && !string.IsNullOrWhiteSpace(body))
            {
                parts.Add(body.Trim());
            }

            for (var index = 0; index < parts.Count; index++)
            {
                var text = parts[index];
                chunks.Add(new ContextChunk
                {
                    ChunkId = $"{documentId}-chunk-{index + 1}",
                    Text = text,
                    SearchText = text.ToLowerInvariant(),
                    Order = index
                });
            }

            return chunks;
        }
    }
}
