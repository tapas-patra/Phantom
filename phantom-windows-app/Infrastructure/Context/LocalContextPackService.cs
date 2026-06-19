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
        private const string LocalDraftPackId = "default";
        private const string LocalDraftPackName = "Local Context Pack";
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
                return ClonePack(selectedPack);
            }

            var fallbackPack = state.Packs[0];
            state.SelectedPackId = fallbackPack.PackId;
            _repository.Save(state);
            return ClonePack(fallbackPack);
        }

        public ContextPack GetLocalDraftPack()
        {
            var state = EnsureState();
            return ClonePack(NormalizeLocalDraftPack(state.LocalDraftPack));
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
            var existingPack = existingIndex >= 0 ? state.Packs[existingIndex] : null;
            var packToSave = PreparePackForSave(pack, existingPack);

            if (existingIndex >= 0)
            {
                state.Packs[existingIndex] = packToSave;
            }
            else
            {
                state.Packs.Add(packToSave);
            }

            state.SelectedPackId = packToSave.PackId;
            _repository.Save(state);
        }

        public void SaveLocalDraftPack(ContextPack pack)
        {
            var state = EnsureState();
            var normalizedLocalDraft = NormalizeLocalDraftPack(pack);
            state.LocalDraftPack = PreparePackForSave(normalizedLocalDraft, NormalizeLocalDraftPack(state.LocalDraftPack));
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
                if (state.LocalDraftPack == null)
                {
                    state.LocalDraftPack = NormalizeLocalDraftPack(state.Packs[0]);
                    _repository.Save(state);
                }
                else if (!string.Equals(state.LocalDraftPack.PackId, LocalDraftPackId, StringComparison.Ordinal)
                    || !string.Equals(state.LocalDraftPack.Name, LocalDraftPackName, StringComparison.Ordinal))
                {
                    state.LocalDraftPack = NormalizeLocalDraftPack(state.LocalDraftPack);
                    _repository.Save(state);
                }

                return state;
            }

            var migrated = CreateLegacyMigratedPack();
            var newState = new ContextPackState
            {
                SelectedPackId = migrated.PackId,
                LocalDraftPack = NormalizeLocalDraftPack(migrated),
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

        private static ContextPack PreparePackForSave(ContextPack pack, ContextPack? existingPack)
        {
            var clone = ClonePack(pack);

            if (existingPack != null)
            {
                if (string.Equals(existingPack.ResumeText, clone.ResumeText, StringComparison.Ordinal))
                {
                    clone.ResumeSummary = string.IsNullOrWhiteSpace(clone.ResumeSummary)
                        ? existingPack.ResumeSummary
                        : clone.ResumeSummary;
                }
                else if (string.IsNullOrWhiteSpace(clone.ResumeSummary))
                {
                    clone.ResumeSummary = string.Empty;
                }

                if (string.Equals(existingPack.JobDescriptionText, clone.JobDescriptionText, StringComparison.Ordinal))
                {
                    clone.JobDescriptionSummary = string.IsNullOrWhiteSpace(clone.JobDescriptionSummary)
                        ? existingPack.JobDescriptionSummary
                        : clone.JobDescriptionSummary;
                }
                else if (string.IsNullOrWhiteSpace(clone.JobDescriptionSummary))
                {
                    clone.JobDescriptionSummary = string.Empty;
                }
            }

            clone.Documents = BuildDocuments(clone);
            clone.UpdatedAtUtc = DateTime.UtcNow;
            return clone;
        }

        private static ContextPack ClonePack(ContextPack pack)
        {
            var clone = new ContextPack
            {
                PackId = pack.PackId,
                Name = pack.Name,
                ResumeText = pack.ResumeText ?? string.Empty,
                ResumeSummary = pack.ResumeSummary ?? string.Empty,
                JobDescriptionText = pack.JobDescriptionText ?? string.Empty,
                JobDescriptionSummary = pack.JobDescriptionSummary ?? string.Empty,
                UpdatedAtUtc = pack.UpdatedAtUtc
            };

            clone.Documents = BuildDocuments(clone);
            return clone;
        }

        private static ContextPack NormalizeLocalDraftPack(ContextPack? source)
        {
            var normalized = ClonePack(source ?? new ContextPack());
            normalized.PackId = LocalDraftPackId;
            normalized.Name = LocalDraftPackName;
            normalized.Documents = BuildDocuments(normalized);
            return normalized;
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
