using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class HostedKnowledgeBaseService
{
    private const int MaxDocumentsPerKnowledgeBase = 20;
    private const int MaxUploadFilesPerRequest = 5;
    private const long MaxUploadBytesPerFile = 2 * 1024 * 1024;
    private const long MaxUploadBytesPerRequest = 8 * 1024 * 1024;
    private const int MaxCharactersPerDocument = 250_000;
    private const int MaxChunksPerKnowledgeBase = 1200;
    private const int MaxChunksPerDocument = 250;
    private const int ChunkTargetSize = 900;
    private const int ChunkMaxSize = 1200;
    private const int ChunkOverlapChars = 160;
    private const int SearchCandidateMultiplier = 12;
    private const int MinSearchCandidateCount = 24;
    private const int MaxSearchCandidateCount = 72;
    private const int MaxSearchCacheEntries = 256;
    private static readonly TimeSpan SearchCacheTtl = TimeSpan.FromMinutes(3);

    private readonly HostedKnowledgeBaseRepository _knowledgeBases;
    private readonly AuthSessionRepository _sessions;
    private readonly AccountRepository _accounts;
    private readonly TokenService _tokens;
    private readonly IKnowledgeBaseEmbeddingService _embeddingService;
    private readonly HostedKnowledgeBaseReindexJobRepository _reindexJobs;
    private readonly ConcurrentDictionary<string, CachedSearchEntry> _searchCache = new(StringComparer.Ordinal);

    public HostedKnowledgeBaseService(
        HostedKnowledgeBaseRepository knowledgeBases,
        HostedKnowledgeBaseReindexJobRepository reindexJobs,
        AuthSessionRepository sessions,
        AccountRepository accounts,
        TokenService tokens,
        IKnowledgeBaseEmbeddingService embeddingService)
    {
        _knowledgeBases = knowledgeBases;
        _reindexJobs = reindexJobs;
        _sessions = sessions;
        _accounts = accounts;
        _tokens = tokens;
        _embeddingService = embeddingService;
    }

    public DesktopAccountRecord RequireAccountFromAccessToken(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new BackendValidationException("Authorization bearer token is required.");
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new BackendValidationException("Authorization bearer token is required.");
        }

        var session = _sessions.FindByAccessTokenHash(_tokens.HashToken(token))
            ?? throw new BackendValidationException("Desktop session not found.");

        if (!session.IsAuthenticated || session.RevokedAtUtc.HasValue || session.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new BackendValidationException("Desktop session is no longer valid.");
        }

        return _accounts.FindByUserId(session.UserId)
            ?? throw new BackendValidationException("Account not found.");
    }

    public HostedKnowledgeBaseSummaryDto GetSummaryForAccount(DesktopAccountRecord account)
    {
        var knowledgeBase = _knowledgeBases.FindByUserId(account.UserId);
        return MapSummary(account, knowledgeBase);
    }

    public HostedKnowledgeBaseSummaryDto CreateOrUpdateKnowledgeBase(DesktopAccountRecord account, HostedKnowledgeBaseCreateRequestDto request)
    {
        EnsureCanManage(account);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new BackendValidationException("Knowledge base name is required.");
        }

        var now = DateTime.UtcNow;
        var existing = _knowledgeBases.FindByUserId(account.UserId);
        var knowledgeBase = existing ?? new HostedKnowledgeBaseRecord
        {
            KnowledgeBaseId = $"kb-{Guid.NewGuid():N}",
            UserId = account.UserId,
            CreatedAtUtc = now
        };

        knowledgeBase.Name = request.Name.Trim();
        knowledgeBase.Description = request.Description?.Trim() ?? string.Empty;
        knowledgeBase.Status = existing?.Status ?? "empty";
        knowledgeBase.EmbeddingModel = existing?.EmbeddingModel ?? _embeddingService.ActiveProfile.ModelId;
        knowledgeBase.EmbeddingVersion = existing?.EmbeddingVersion ?? _embeddingService.ActiveProfile.Version;
        knowledgeBase.UpdatedAtUtc = now;

        _knowledgeBases.SaveKnowledgeBase(knowledgeBase);
        return MapSummary(account, knowledgeBase);
    }

    public async Task<HostedKnowledgeBaseUploadResultDto> UploadDocumentsAsync(
        DesktopAccountRecord account,
        IFormFileCollection files,
        CancellationToken cancellationToken)
    {
        EnsureCanManage(account);
        EnsureEmbeddingsConfigured();

        if (files.Count == 0)
        {
            throw new BackendValidationException("At least one document file is required.");
        }

        if (files.Count > MaxUploadFilesPerRequest)
        {
            throw new BackendValidationException($"Upload at most {MaxUploadFilesPerRequest} documents per request.");
        }

        var totalUploadBytes = files.Sum(file => file.Length);
        if (totalUploadBytes > MaxUploadBytesPerRequest)
        {
            throw new BackendValidationException("The combined upload exceeds the 8 MB per-request limit.");
        }

        var now = DateTime.UtcNow;
        var knowledgeBase = _knowledgeBases.FindByUserId(account.UserId) ?? new HostedKnowledgeBaseRecord
        {
            KnowledgeBaseId = $"kb-{Guid.NewGuid():N}",
            UserId = account.UserId,
            Name = "Premium Knowledge Base",
            Description = "Hosted interview knowledge base",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var existingDocuments = _knowledgeBases.ListDocuments(knowledgeBase.KnowledgeBaseId).ToList();
        var existingChunks = _knowledgeBases.ListChunks(knowledgeBase.KnowledgeBaseId).ToList();
        var nextDocuments = new List<HostedKnowledgeBaseDocumentRecord>();
        var nextChunks = new List<HostedKnowledgeBaseChunkRecord>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.Length <= 0)
            {
                throw new BackendValidationException($"'{file.FileName}' is empty.");
            }

            if (file.Length > MaxUploadBytesPerFile)
            {
                throw new BackendValidationException($"'{file.FileName}' exceeds the 2 MB Premium knowledge-base upload limit.");
            }

            var extraction = await ExtractTextAsync(file, cancellationToken);
            var extractedText = NormalizeSourceText(extraction.Text);
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                throw new BackendValidationException($"'{file.FileName}' did not produce readable text.");
            }

            if (extractedText.Length > MaxCharactersPerDocument)
            {
                throw new BackendValidationException(
                    $"'{file.FileName}' exceeds the {MaxCharactersPerDocument:N0} character extraction limit.");
            }

            var documentId = $"kb-doc-{Guid.NewGuid():N}";
            var contentSha = ComputeSha256(extractedText);
            var documentChunks = BuildChunks(knowledgeBase, account, documentId, file.FileName, extraction.SourceType, extractedText, now);
            if (documentChunks.Count > MaxChunksPerDocument)
            {
                throw new BackendValidationException(
                    $"'{file.FileName}' exceeds the per-document chunk limit of {MaxChunksPerDocument}.");
            }

            var document = new HostedKnowledgeBaseDocumentRecord
            {
                DocumentId = documentId,
                KnowledgeBaseId = knowledgeBase.KnowledgeBaseId,
                UserId = account.UserId,
                FileName = file.FileName,
                ContentType = file.ContentType ?? string.Empty,
                SourceType = extraction.SourceType,
                ExtractedText = extractedText,
                ContentSha256 = contentSha,
                CharacterCount = extractedText.Length,
                ChunkCount = documentChunks.Count,
                Status = "processing",
                Error = string.Empty,
                UploadedAtUtc = now
            };

            nextDocuments.Add(document);
            nextChunks.AddRange(documentChunks);
        }

        var mergedDocuments = existingDocuments.Concat(nextDocuments).ToList();
        var mergedChunks = existingChunks.Concat(nextChunks).ToList();
        EnsureKnowledgeBaseLimits(mergedDocuments, mergedChunks);

        await IndexChunksAsync(nextDocuments, nextChunks, cancellationToken);

        knowledgeBase.DocumentCount = mergedDocuments.Count;
        knowledgeBase.ChunkCount = mergedChunks.Count;
        knowledgeBase.EmbeddingModel = _embeddingService.ActiveProfile.ModelId;
        knowledgeBase.EmbeddingVersion = _embeddingService.ActiveProfile.Version;
        knowledgeBase.LastProcessedAtUtc = now;
        knowledgeBase.Status = mergedChunks.Count > 0 ? "ready" : "empty";
        knowledgeBase.UpdatedAtUtc = now;

        _knowledgeBases.ReplaceDocumentsAndChunks(knowledgeBase, mergedDocuments, mergedChunks);
        InvalidateSearchCache(knowledgeBase.KnowledgeBaseId);

        return new HostedKnowledgeBaseUploadResultDto
        {
            KnowledgeBase = MapSummary(account, knowledgeBase, mergedDocuments),
            AddedDocuments = nextDocuments.Select(MapDocument).ToArray()
        };
    }

    public HostedKnowledgeBaseReindexJobDto QueueReindex(
        DesktopAccountRecord account)
    {
        EnsureCanManage(account);
        EnsureEmbeddingsConfigured();

        var knowledgeBase = _knowledgeBases.FindByUserId(account.UserId);
        if (knowledgeBase == null)
        {
            throw new BackendValidationException("No hosted knowledge base exists for this account.");
        }

        var existingDocuments = _knowledgeBases.ListDocuments(knowledgeBase.KnowledgeBaseId).ToList();
        if (existingDocuments.Count == 0)
        {
            throw new BackendValidationException("No hosted knowledge-base documents are available to reindex.");
        }

        var activeJob = _reindexJobs.FindActiveForKnowledgeBase(knowledgeBase.KnowledgeBaseId);
        if (activeJob != null)
        {
            return MapReindexJob(activeJob)!;
        }

        var profile = _embeddingService.ActiveProfile;
        var now = DateTime.UtcNow;
        var job = new HostedKnowledgeBaseReindexJobRecord
        {
            JobId = $"kb-reindex-{Guid.NewGuid():N}",
            KnowledgeBaseId = knowledgeBase.KnowledgeBaseId,
            UserId = account.UserId,
            Status = "queued",
            Error = string.Empty,
            TargetEmbeddingModel = profile.ModelId,
            TargetEmbeddingVersion = profile.Version,
            TotalDocuments = existingDocuments.Count,
            ProcessedDocuments = 0,
            RequestedAtUtc = now,
            UpdatedAtUtc = now
        };
        if (!_reindexJobs.Enqueue(job))
        {
            return MapReindexJob(_reindexJobs.FindActiveForKnowledgeBase(knowledgeBase.KnowledgeBaseId))
                ?? MapReindexJob(job)!;
        }

        return MapReindexJob(job)!;
    }

    public HostedKnowledgeBaseReindexJobDto? GetLatestReindexJob(DesktopAccountRecord account, string? jobId = null)
    {
        var knowledgeBase = _knowledgeBases.FindByUserId(account.UserId);
        if (knowledgeBase == null)
        {
            return null;
        }

        var job = string.IsNullOrWhiteSpace(jobId)
            ? _reindexJobs.FindLatestForKnowledgeBase(knowledgeBase.KnowledgeBaseId)
            : _reindexJobs.FindByJobIdForUser(jobId, account.UserId);
        return job == null ? null : MapReindexJob(job);
    }

    public async Task<bool> TryProcessNextReindexJobAsync(CancellationToken cancellationToken)
    {
        var job = _reindexJobs.TryStartNextQueued();
        if (job == null)
        {
            return false;
        }

        try
        {
            await ProcessReindexJobAsync(job, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _reindexJobs.MarkFailed(job.JobId, ex.Message);
        }

        return true;
    }

    public async Task<HostedKnowledgeBaseSearchResultDto> SearchAsync(
        DesktopAccountRecord account,
        string query,
        int maxSnippets,
        CancellationToken cancellationToken)
    {
        EnsureCanUseInInterview(account);

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new BackendValidationException("Search query is required.");
        }

        var knowledgeBase = _knowledgeBases.FindByUserId(account.UserId);
        if (knowledgeBase == null || knowledgeBase.DocumentCount == 0 || knowledgeBase.ChunkCount == 0)
        {
            throw new BackendValidationException("No hosted knowledge base is ready for this account.");
        }

        var normalizedQuery = NormalizeChunkText(query);
        var snippetLimit = Math.Clamp(maxSnippets, 1, 6);
        var activeEmbeddingProfile = $"{_embeddingService.ActiveProfile.ModelId}:{_embeddingService.ActiveProfile.Version}";
        var cacheKey = BuildSearchCacheKey(knowledgeBase.KnowledgeBaseId, activeEmbeddingProfile, normalizedQuery, snippetLimit);
        if (TryGetCachedSearch(cacheKey, out var cachedSnippets))
        {
            return new HostedKnowledgeBaseSearchResultDto
            {
                KnowledgeBase = MapSummary(account, knowledgeBase),
                Snippets = cachedSnippets
            };
        }

        string? queryVectorLiteral = null;
        if (_embeddingService.IsConfigured)
        {
            try
            {
                var queryVector = await _embeddingService.GenerateEmbeddingAsync(normalizedQuery, cancellationToken);
                if (queryVector.Length == HostedKnowledgeBaseEmbeddingDefaults.DefaultDimensions)
                {
                    queryVectorLiteral = ToVectorLiteral(queryVector);
                }
            }
            catch
            {
                queryVectorLiteral = null;
            }
        }

        var candidateLimit = Math.Clamp(
            snippetLimit * SearchCandidateMultiplier,
            MinSearchCandidateCount,
            MaxSearchCandidateCount);
        var candidates = _knowledgeBases.SearchHybridCandidates(
            knowledgeBase.KnowledgeBaseId,
            normalizedQuery,
            queryVectorLiteral,
            _embeddingService.ActiveProfile.ModelId,
            _embeddingService.ActiveProfile.Version,
            lexicalLimit: candidateLimit,
            semanticLimit: candidateLimit,
            finalLimit: candidateLimit);
        var terms = Tokenize(normalizedQuery).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var snippets = candidates
            .Select(candidate => new HostedKnowledgeBaseSnippetDto
            {
                DocumentId = candidate.DocumentId,
                DocumentTitle = candidate.DocumentTitle,
                Text = candidate.Text,
                Score = ScoreCandidate(candidate, terms)
            })
            .Where(item => item.Score > 0d)
            .OrderByDescending(item => item.Score)
            .Take(snippetLimit)
            .ToArray();

        _searchCache[cacheKey] = new CachedSearchEntry
        {
            CachedAtUtc = DateTime.UtcNow,
            Snippets = snippets
        };
        TrimSearchCacheIfNeeded();

        return new HostedKnowledgeBaseSearchResultDto
        {
            KnowledgeBase = MapSummary(account, knowledgeBase),
            Snippets = snippets
        };
    }

    private HostedKnowledgeBaseSummaryDto MapSummary(
        DesktopAccountRecord account,
        HostedKnowledgeBaseRecord? knowledgeBase,
        IReadOnlyList<HostedKnowledgeBaseDocumentRecord>? documents = null)
    {
        var canManage = HasPremiumKnowledgeBaseEntitlement(account, out var blockedReason);
        var documentList = documents
            ?? (knowledgeBase == null ? Array.Empty<HostedKnowledgeBaseDocumentRecord>() : _knowledgeBases.ListDocuments(knowledgeBase.KnowledgeBaseId));

        if (knowledgeBase == null)
        {
            return new HostedKnowledgeBaseSummaryDto
            {
                Status = "not_created",
                EmbeddingModel = _embeddingService.ActiveProfile.ModelId,
                EmbeddingVersion = _embeddingService.ActiveProfile.Version,
                CanManage = canManage,
                CanUseInInterview = false,
                BlockedReason = blockedReason,
                Documents = Array.Empty<HostedKnowledgeBaseDocumentDto>()
            };
        }

        return new HostedKnowledgeBaseSummaryDto
        {
            KnowledgeBaseId = knowledgeBase.KnowledgeBaseId,
            Name = knowledgeBase.Name,
            Description = knowledgeBase.Description,
            Status = knowledgeBase.Status,
            EmbeddingModel = knowledgeBase.EmbeddingModel,
            EmbeddingVersion = knowledgeBase.EmbeddingVersion,
            DocumentCount = knowledgeBase.DocumentCount,
            ChunkCount = knowledgeBase.ChunkCount,
            CanManage = canManage,
            CanUseInInterview = canManage
                && string.Equals(knowledgeBase.Status, "ready", StringComparison.OrdinalIgnoreCase)
                && knowledgeBase.ChunkCount > 0,
            BlockedReason = blockedReason,
            LastProcessedAtUtc = knowledgeBase.LastProcessedAtUtc,
            LatestReindexJob = MapReindexJob(_reindexJobs.FindLatestForKnowledgeBase(knowledgeBase.KnowledgeBaseId)),
            Documents = documentList.Select(MapDocument).ToArray()
        };
    }

    private static HostedKnowledgeBaseReindexJobDto? MapReindexJob(HostedKnowledgeBaseReindexJobRecord? job)
    {
        if (job == null)
        {
            return null;
        }

        return new HostedKnowledgeBaseReindexJobDto
        {
            JobId = job.JobId,
            KnowledgeBaseId = job.KnowledgeBaseId,
            Status = job.Status,
            Error = job.Error,
            TargetEmbeddingModel = job.TargetEmbeddingModel,
            TargetEmbeddingVersion = job.TargetEmbeddingVersion,
            TotalDocuments = job.TotalDocuments,
            ProcessedDocuments = job.ProcessedDocuments,
            RequestedAtUtc = job.RequestedAtUtc,
            StartedAtUtc = job.StartedAtUtc,
            CompletedAtUtc = job.CompletedAtUtc,
            UpdatedAtUtc = job.UpdatedAtUtc
        };
    }

    private static HostedKnowledgeBaseDocumentDto MapDocument(HostedKnowledgeBaseDocumentRecord document)
    {
        return new HostedKnowledgeBaseDocumentDto
        {
            DocumentId = document.DocumentId,
            FileName = document.FileName,
            ContentType = document.ContentType,
            SourceType = document.SourceType,
            EmbeddingModel = document.EmbeddingModel,
            EmbeddingVersion = document.EmbeddingVersion,
            CharacterCount = document.CharacterCount,
            ChunkCount = document.ChunkCount,
            Status = document.Status,
            Error = document.Error,
            UploadedAtUtc = document.UploadedAtUtc,
            ProcessedAtUtc = document.ProcessedAtUtc,
            IndexedAtUtc = document.IndexedAtUtc
        };
    }

    private static bool HasPremiumKnowledgeBaseEntitlement(DesktopAccountRecord account, out string blockedReason)
    {
        if (!AccessModeResolver.HasPremiumFeatureAccess(account))
        {
            blockedReason = "Hosted knowledge bases are a Premium-only feature.";
            return false;
        }

        if (account.PremiumNegativeCredits > 0m)
        {
            blockedReason = "Hosted knowledge-base access is blocked while Premium debt is outstanding.";
            return false;
        }

        if (account.PremiumAvailableCredits <= 0m)
        {
            blockedReason = "Hosted knowledge-base access requires available Premium credits.";
            return false;
        }

        blockedReason = string.Empty;
        return true;
    }

    private static void EnsureCanManage(DesktopAccountRecord account)
    {
        if (!HasPremiumKnowledgeBaseEntitlement(account, out var blockedReason))
        {
            throw new BackendValidationException(blockedReason);
        }
    }

    private static void EnsureCanUseInInterview(DesktopAccountRecord account)
    {
        if (!HasPremiumKnowledgeBaseEntitlement(account, out var blockedReason))
        {
            throw new BackendValidationException(blockedReason);
        }
    }

    private static void EnsureKnowledgeBaseLimits(
        IReadOnlyList<HostedKnowledgeBaseDocumentRecord> documents,
        IReadOnlyList<HostedKnowledgeBaseChunkRecord> chunks)
    {
        if (documents.Count > MaxDocumentsPerKnowledgeBase)
        {
            throw new BackendValidationException($"Premium knowledge bases support up to {MaxDocumentsPerKnowledgeBase} documents.");
        }

        if (chunks.Count > MaxChunksPerKnowledgeBase)
        {
            throw new BackendValidationException($"The uploaded files exceed the Premium knowledge-base chunk limit of {MaxChunksPerKnowledgeBase}.");
        }

        var oversizedDocument = documents.FirstOrDefault(document => document.ChunkCount > MaxChunksPerDocument);
        if (oversizedDocument != null)
        {
            throw new BackendValidationException(
                $"'{oversizedDocument.FileName}' exceeds the per-document chunk limit of {MaxChunksPerDocument}.");
        }
    }

    private void EnsureEmbeddingsConfigured()
    {
        if (!_embeddingService.IsConfigured)
        {
            throw new BackendValidationException("Hosted knowledge-base embeddings are not configured.");
        }
    }

    private async Task IndexChunksAsync(
        IReadOnlyList<HostedKnowledgeBaseDocumentRecord> documents,
        IReadOnlyList<HostedKnowledgeBaseChunkRecord> chunks,
        CancellationToken cancellationToken)
    {
        var profile = _embeddingService.ActiveProfile;
        var embeddings = await _embeddingService.GenerateEmbeddingsAsync(chunks.Select(chunk => chunk.Text).ToArray(), cancellationToken);
        if (embeddings.Count != chunks.Count)
        {
            throw new BackendValidationException("Knowledge-base embedding count mismatch during indexing.");
        }

        var now = DateTime.UtcNow;
        for (var index = 0; index < chunks.Count; index++)
        {
            chunks[index].EmbeddingVector = ToVectorLiteral(embeddings[index]);
            chunks[index].EmbeddingModel = profile.ModelId;
            chunks[index].EmbeddingVersion = profile.Version;
            chunks[index].IndexedAtUtc = now;
        }

        foreach (var document in documents)
        {
            document.EmbeddingModel = profile.ModelId;
            document.EmbeddingVersion = profile.Version;
            document.Status = "ready";
            document.Error = string.Empty;
            document.ProcessedAtUtc = now;
            document.IndexedAtUtc = now;
        }
    }

    private async Task ProcessReindexJobAsync(
        HostedKnowledgeBaseReindexJobRecord job,
        CancellationToken cancellationToken)
    {
        var account = _accounts.FindByUserId(job.UserId)
            ?? throw new BackendValidationException("Account not found for reindex job.");
        var knowledgeBase = _knowledgeBases.FindByUserId(account.UserId)
            ?? throw new BackendValidationException("Knowledge base not found for reindex job.");
        var existingDocuments = _knowledgeBases.ListDocuments(knowledgeBase.KnowledgeBaseId).ToList();
        if (existingDocuments.Count == 0)
        {
            throw new BackendValidationException("No hosted knowledge-base documents are available to reindex.");
        }

        var missingText = existingDocuments
            .Where(document => string.IsNullOrWhiteSpace(document.ExtractedText))
            .Select(document => document.FileName)
            .ToArray();
        if (missingText.Length > 0)
        {
            throw new BackendValidationException(
                $"These documents cannot be reindexed because extracted text is unavailable: {string.Join(", ", missingText)}.");
        }

        var now = DateTime.UtcNow;
        var rebuiltDocuments = new List<HostedKnowledgeBaseDocumentRecord>();
        var rebuiltChunks = new List<HostedKnowledgeBaseChunkRecord>();
        _reindexJobs.UpdateProgress(job.JobId, existingDocuments.Count, 0);

        var processedDocuments = 0;
        foreach (var document in existingDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extractedText = NormalizeSourceText(document.ExtractedText);
            var contentSha = string.IsNullOrWhiteSpace(document.ContentSha256)
                ? ComputeSha256(extractedText)
                : document.ContentSha256;
            var documentChunks = BuildChunks(
                knowledgeBase,
                account,
                document.DocumentId,
                document.FileName,
                document.SourceType,
                extractedText,
                now);

            rebuiltDocuments.Add(new HostedKnowledgeBaseDocumentRecord
            {
                DocumentId = document.DocumentId,
                KnowledgeBaseId = document.KnowledgeBaseId,
                UserId = document.UserId,
                FileName = document.FileName,
                ContentType = document.ContentType,
                SourceType = document.SourceType,
                ExtractedText = extractedText,
                ContentSha256 = contentSha,
                CharacterCount = extractedText.Length,
                ChunkCount = documentChunks.Count,
                Status = "processing",
                Error = string.Empty,
                UploadedAtUtc = document.UploadedAtUtc,
                ProcessedAtUtc = null,
                IndexedAtUtc = null
            });
            rebuiltChunks.AddRange(documentChunks);
            processedDocuments++;
            _reindexJobs.UpdateProgress(job.JobId, existingDocuments.Count, processedDocuments);
        }

        EnsureKnowledgeBaseLimits(rebuiltDocuments, rebuiltChunks);
        await IndexChunksAsync(rebuiltDocuments, rebuiltChunks, cancellationToken);

        knowledgeBase.DocumentCount = rebuiltDocuments.Count;
        knowledgeBase.ChunkCount = rebuiltChunks.Count;
        knowledgeBase.EmbeddingModel = _embeddingService.ActiveProfile.ModelId;
        knowledgeBase.EmbeddingVersion = _embeddingService.ActiveProfile.Version;
        knowledgeBase.LastProcessedAtUtc = now;
        knowledgeBase.Status = rebuiltChunks.Count > 0 ? "ready" : "empty";
        knowledgeBase.UpdatedAtUtc = now;

        _knowledgeBases.ReplaceDocumentsAndChunks(knowledgeBase, rebuiltDocuments, rebuiltChunks);
        InvalidateSearchCache(knowledgeBase.KnowledgeBaseId);
        _reindexJobs.MarkCompleted(job.JobId);
    }

    private static async Task<(string Text, string SourceType)> ExtractTextAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        await using var sourceStream = file.OpenReadStream();

        return extension switch
        {
            ".txt" or ".md" or ".json" or ".csv" or ".log" => (await ReadUtf8Async(sourceStream, cancellationToken), extension.TrimStart('.')),
            ".docx" => (await ExtractDocxAsync(sourceStream, cancellationToken), "docx"),
            _ => throw new BackendValidationException($"'{file.FileName}' uses an unsupported document type. Supported types: txt, md, json, csv, log, docx.")
        };
    }

    private static async Task<string> ReadUtf8Async(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return NormalizeSourceText(await reader.ReadToEndAsync(cancellationToken));
    }

    private static async Task<string> ExtractDocxAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new BackendValidationException("The .docx file is missing word/document.xml.");
        await using var entryStream = entry.Open();
        var document = await XDocument.LoadAsync(entryStream, LoadOptions.None, cancellationToken);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var paragraphs = document.Descendants(w + "p")
            .Select(paragraph =>
                string.Concat(paragraph.Descendants(w + "t").Select(element => element.Value)).Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return NormalizeSourceText(string.Join("\n\n", paragraphs));
    }

    private static List<HostedKnowledgeBaseChunkRecord> BuildChunks(
        HostedKnowledgeBaseRecord knowledgeBase,
        DesktopAccountRecord account,
        string documentId,
        string documentTitle,
        string sourceType,
        string text,
        DateTime createdAtUtc)
    {
        var units = BuildSemanticUnits(text);
        var chunks = new List<HostedKnowledgeBaseChunkRecord>();
        if (units.Count == 0)
        {
            return chunks;
        }

        var current = new StringBuilder();
        var index = 0;
        foreach (var unit in units)
        {
            if (current.Length == 0)
            {
                current.Append(unit);
                continue;
            }

            if (current.Length + 2 + unit.Length <= ChunkTargetSize)
            {
                current.Append("\n\n").Append(unit);
                continue;
            }

            AddChunk(chunks, knowledgeBase, account, documentId, documentTitle, sourceType, current.ToString(), index++, createdAtUtc);
            var overlap = BuildOverlap(current.ToString());
            current.Clear();
            if (!string.IsNullOrWhiteSpace(overlap))
            {
                current.Append(overlap).Append("\n\n");
            }

            current.Append(unit);
            while (current.Length > ChunkMaxSize)
            {
                var sliceLength = Math.Min(ChunkMaxSize, current.Length);
                AddChunk(chunks, knowledgeBase, account, documentId, documentTitle, sourceType, current.ToString(0, sliceLength), index++, createdAtUtc);
                var tail = current.ToString(Math.Max(0, sliceLength - ChunkOverlapChars), current.Length - Math.Max(0, sliceLength - ChunkOverlapChars));
                current.Clear();
                current.Append(tail.Trim());
            }
        }

        if (current.Length > 0)
        {
            AddChunk(chunks, knowledgeBase, account, documentId, documentTitle, sourceType, current.ToString(), index, createdAtUtc);
        }

        return chunks;
    }

    private static void AddChunk(
        List<HostedKnowledgeBaseChunkRecord> chunks,
        HostedKnowledgeBaseRecord knowledgeBase,
        DesktopAccountRecord account,
        string documentId,
        string documentTitle,
        string sourceType,
        string rawChunkText,
        int chunkIndex,
        DateTime createdAtUtc)
    {
        var chunkText = NormalizeChunkText(rawChunkText);
        if (chunkText.Length == 0)
        {
            return;
        }

        var sectionTitle = ExtractSectionTitle(rawChunkText, documentTitle);
        chunks.Add(new HostedKnowledgeBaseChunkRecord
        {
            ChunkId = $"kb-chunk-{Guid.NewGuid():N}",
            KnowledgeBaseId = knowledgeBase.KnowledgeBaseId,
            DocumentId = documentId,
            UserId = account.UserId,
            ChunkIndex = chunkIndex,
            DocumentTitle = documentTitle,
            SectionTitle = sectionTitle,
            Text = chunkText,
            SearchText = chunkText.ToLowerInvariant(),
            ContentSha256 = ComputeSha256(chunkText),
            MetadataJson = JsonSerializer.Serialize(new { sourceType, sectionTitle }),
            TokenCount = Tokenize(chunkText).Count,
            CreatedAtUtc = createdAtUtc
        });
    }

    private static List<string> BuildSemanticUnits(string text)
    {
        var normalized = NormalizeSourceText(text);
        var paragraphs = Regex.Split(normalized, @"\n\s*\n+")
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .ToList();
        if (paragraphs.Count == 0)
        {
            return new List<string>();
        }

        var units = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length <= ChunkMaxSize)
            {
                units.Add(paragraph);
                continue;
            }

            var sentences = Regex.Split(paragraph, @"(?<=[.!?])\s+")
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .ToArray();
            if (sentences.Length == 0)
            {
                units.Add(paragraph);
                continue;
            }

            var current = new StringBuilder();
            foreach (var sentence in sentences)
            {
                if (current.Length == 0)
                {
                    current.Append(sentence);
                }
                else if (current.Length + 1 + sentence.Length <= ChunkTargetSize)
                {
                    current.Append(' ').Append(sentence);
                }
                else
                {
                    units.Add(current.ToString());
                    current.Clear();
                    current.Append(sentence);
                }
            }

            if (current.Length > 0)
            {
                units.Add(current.ToString());
            }
        }

        return units;
    }

    private static string BuildOverlap(string chunkText)
    {
        var normalized = NormalizeChunkText(chunkText);
        if (normalized.Length <= ChunkOverlapChars)
        {
            return normalized;
        }

        var tail = normalized[^ChunkOverlapChars..].Trim();
        var firstSpace = tail.IndexOf(' ');
        return firstSpace > 0 ? tail[(firstSpace + 1)..].Trim() : tail;
    }

    private static string ExtractSectionTitle(string chunkText, string documentTitle)
    {
        var normalizedLines = NormalizeSourceText(chunkText)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (normalizedLines.Length > 0)
        {
            var firstLine = NormalizeChunkText(normalizedLines[0]);
            if (firstLine.Length > 0)
            {
                return firstLine.Length <= 90 ? firstLine : $"{firstLine[..87].TrimEnd()}...";
            }
        }

        return documentTitle;
    }

    private static double ScoreCandidate(HostedKnowledgeBaseSearchCandidateRecord candidate, string[] terms)
    {
        var keywordHits = terms.Length == 0
            ? 0d
            : terms.Count(term => candidate.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)) / (double)terms.Length;
        var sectionBoost = terms.Any(term => candidate.SectionTitle.Contains(term, StringComparison.OrdinalIgnoreCase)) ? 0.05d : 0d;
        return candidate.FusedScore
            + (candidate.SemanticSimilarity * 0.35d)
            + (candidate.LexicalScore * 0.10d)
            + (keywordHits * 0.20d)
            + sectionBoost;
    }

    private static string NormalizeSourceText(string value)
    {
        return (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static string NormalizeChunkText(string value)
    {
        return Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
    }

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ToVectorLiteral(IReadOnlyList<float> values)
    {
        return $"[{string.Join(",", values.Select(value => value.ToString("G9", CultureInfo.InvariantCulture)))}]";
    }

    private static List<string> Tokenize(string value)
    {
        return Regex.Split((value ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9+#.]+")
            .Where(token => token.Length >= 3)
            .ToList();
    }

    private static string BuildSearchCacheKey(string knowledgeBaseId, string embeddingProfileKey, string normalizedQuery, int maxSnippets)
    {
        return $"{knowledgeBaseId}:{embeddingProfileKey}:{maxSnippets}:{normalizedQuery}";
    }

    private bool TryGetCachedSearch(string cacheKey, out IReadOnlyList<HostedKnowledgeBaseSnippetDto> snippets)
    {
        if (_searchCache.TryGetValue(cacheKey, out var cached)
            && DateTime.UtcNow - cached.CachedAtUtc <= SearchCacheTtl)
        {
            snippets = cached.Snippets;
            return true;
        }

        _searchCache.TryRemove(cacheKey, out _);
        snippets = Array.Empty<HostedKnowledgeBaseSnippetDto>();
        return false;
    }

    private void InvalidateSearchCache(string knowledgeBaseId)
    {
        foreach (var key in _searchCache.Keys)
        {
            if (key.StartsWith($"{knowledgeBaseId}:", StringComparison.Ordinal))
            {
                _searchCache.TryRemove(key, out _);
            }
        }
    }

    private void TrimSearchCacheIfNeeded()
    {
        if (_searchCache.Count <= MaxSearchCacheEntries)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var entry in _searchCache)
        {
            if (now - entry.Value.CachedAtUtc > SearchCacheTtl)
            {
                _searchCache.TryRemove(entry.Key, out _);
            }
        }

        if (_searchCache.Count <= MaxSearchCacheEntries)
        {
            return;
        }

        foreach (var entry in _searchCache.OrderBy(item => item.Value.CachedAtUtc).Take(_searchCache.Count - MaxSearchCacheEntries))
        {
            _searchCache.TryRemove(entry.Key, out _);
        }
    }

    private sealed class CachedSearchEntry
    {
        public DateTime CachedAtUtc { get; init; }
        public IReadOnlyList<HostedKnowledgeBaseSnippetDto> Snippets { get; init; } = Array.Empty<HostedKnowledgeBaseSnippetDto>();
    }
}
