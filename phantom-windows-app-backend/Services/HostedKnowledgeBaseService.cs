using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

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
    private const int SearchCandidateMultiplier = 8;
    private const int MinSearchCandidateCount = 12;
    private const int MaxSearchCandidateCount = 36;
    private const int MaxSearchCacheEntries = 256;
    private const double MinSnippetScore = 0.18d;
    private const double MinSemanticSimilarity = 0.45d;
    private const int FallbackSnippetCount = 2;
    private const int MaxSnippetLength = 480;
    private const int MaxSnippetsPerDocument = 2;
    private const string CardSyncDocumentIdPrefix = "kb-card-sync-";
    private static readonly TimeSpan SearchCacheTtl = TimeSpan.FromMinutes(3);

    private readonly HostedKnowledgeBaseRepository _knowledgeBases;
    private readonly AuthSessionRepository _sessions;
    private readonly AccountRepository _accounts;
    private readonly TokenService _tokens;
    private readonly IKnowledgeBaseEmbeddingService _embeddingService;
    private readonly HostedKnowledgeBaseStructuredExtractionService _structuredExtraction;
    private readonly HostedKnowledgeBaseReindexJobRepository _reindexJobs;
    private readonly ConcurrentDictionary<string, CachedSearchEntry> _searchCache = new(StringComparer.Ordinal);
    private readonly ILogger<HostedKnowledgeBaseService> _logger;

    public HostedKnowledgeBaseService(
        HostedKnowledgeBaseRepository knowledgeBases,
        HostedKnowledgeBaseReindexJobRepository reindexJobs,
        AuthSessionRepository sessions,
        AccountRepository accounts,
        TokenService tokens,
        IKnowledgeBaseEmbeddingService embeddingService,
        HostedKnowledgeBaseStructuredExtractionService structuredExtraction,
        ILogger<HostedKnowledgeBaseService> logger)
    {
        _knowledgeBases = knowledgeBases;
        _reindexJobs = reindexJobs;
        _sessions = sessions;
        _accounts = accounts;
        _tokens = tokens;
        _embeddingService = embeddingService;
        _structuredExtraction = structuredExtraction;
        _logger = logger;
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

    public HostedKnowledgeBaseProfileCardDto GetProfileCard(DesktopAccountRecord account)
    {
        var knowledgeBase = _knowledgeBases.FindByUserId(account.UserId);
        if (knowledgeBase == null)
        {
            return new HostedKnowledgeBaseProfileCardDto();
        }

        var profileCard = _knowledgeBases.FindProfileCard(knowledgeBase.KnowledgeBaseId);
        return profileCard == null ? new HostedKnowledgeBaseProfileCardDto() : MapProfileCard(profileCard);
    }

    public IReadOnlyList<HostedKnowledgeBaseProjectCardDto> ListProjectCards(DesktopAccountRecord account)
    {
        var knowledgeBase = _knowledgeBases.FindByUserId(account.UserId);
        if (knowledgeBase == null)
        {
            return Array.Empty<HostedKnowledgeBaseProjectCardDto>();
        }

        return _knowledgeBases.ListProjectCards(knowledgeBase.KnowledgeBaseId)
            .Select(MapProjectCard)
            .ToArray();
    }

    public HostedKnowledgeBaseProjectCardDto GetProjectCard(DesktopAccountRecord account, string projectCardId)
    {
        if (string.IsNullOrWhiteSpace(projectCardId))
        {
            throw new BackendValidationException("Project card ID is required.");
        }

        var knowledgeBase = _knowledgeBases.FindByUserId(account.UserId)
            ?? throw new BackendValidationException("No hosted knowledge base exists for this account.");
        var projectCard = _knowledgeBases.FindProjectCard(knowledgeBase.KnowledgeBaseId, projectCardId.Trim())
            ?? throw new BackendValidationException("Hosted knowledge-base project card not found.");
        return MapProjectCard(projectCard);
    }

    public HostedKnowledgeBaseDocumentContentDto GetDocumentContent(
        DesktopAccountRecord account,
        string documentId)
    {
        EnsureCanManage(account);

        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new BackendValidationException("Document ID is required.");
        }

        var knowledgeBase = _knowledgeBases.FindByUserId(account.UserId)
            ?? throw new BackendValidationException("No hosted knowledge base exists for this account.");
        var document = _knowledgeBases.FindDocument(knowledgeBase.KnowledgeBaseId, documentId.Trim())
            ?? throw new BackendValidationException("Hosted knowledge-base document not found.");

        return new HostedKnowledgeBaseDocumentContentDto
        {
            DocumentId = document.DocumentId,
            FileName = document.FileName,
            ContentType = document.ContentType,
            SourceType = document.SourceType,
            Section = document.Section,
            SourceKind = document.SourceKind,
            SourceLabel = document.SourceLabel,
            ExtractedText = document.ExtractedText,
            CharacterCount = document.CharacterCount,
            ChunkCount = document.ChunkCount,
            Status = document.Status,
            Error = document.Error,
            UploadedAtUtc = document.UploadedAtUtc,
            ProcessedAtUtc = document.ProcessedAtUtc,
            IndexedAtUtc = document.IndexedAtUtc
        };
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
        string section,
        CancellationToken cancellationToken)
    {
        EnsureCanManage(account);
        EnsureEmbeddingsConfigured();
        var normalizedSection = NormalizeSection(section);

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
        var existingChunks = _knowledgeBases.ListChunks(knowledgeBase.KnowledgeBaseId)
            .Where(chunk => !IsCardSyncDocumentId(chunk.DocumentId))
            .ToList();
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
            var uploadFileName = string.IsNullOrWhiteSpace(file.FileName) ? "document" : file.FileName;
            var sourceLabel = Path.GetFileNameWithoutExtension(uploadFileName);
            var documentTitle = string.IsNullOrWhiteSpace(sourceLabel) ? uploadFileName : sourceLabel;
            var documentChunks = BuildChunks(
                knowledgeBase,
                account,
                documentId,
                documentTitle,
                extraction.SourceType,
                extractedText,
                now);
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
                FileName = uploadFileName,
                ContentType = file.ContentType ?? string.Empty,
                SourceType = extraction.SourceType,
                Section = normalizedSection,
                SourceKind = "upload",
                SourceLabel = documentTitle,
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
        knowledgeBase.EmbeddingModel = _embeddingService.ActiveProfile.ModelId;
        knowledgeBase.EmbeddingVersion = _embeddingService.ActiveProfile.Version;
        knowledgeBase.LastProcessedAtUtc = now;
        knowledgeBase.UpdatedAtUtc = now;

        var structuredMemory = await _structuredExtraction.ExtractAsync(account, knowledgeBase, mergedDocuments, cancellationToken);
        var cardSyncChunks = await BuildStructuredCardSyncChunksAsync(
            account,
            knowledgeBase,
            structuredMemory.ProfileCard,
            structuredMemory.ProjectCards,
            cancellationToken);
        var persistedChunks = mergedChunks.Concat(cardSyncChunks).ToList();
        knowledgeBase.ChunkCount = persistedChunks.Count;
        knowledgeBase.Status = persistedChunks.Count > 0 ? "ready" : "empty";
        _knowledgeBases.ReplaceDocumentsAndChunks(
            knowledgeBase,
            mergedDocuments,
            persistedChunks,
            structuredMemory.ProfileCard,
            structuredMemory.ProjectCards);
        InvalidateSearchCache(knowledgeBase.KnowledgeBaseId);

        return new HostedKnowledgeBaseUploadResultDto
        {
            KnowledgeBase = MapSummary(
                account,
                knowledgeBase,
                mergedDocuments,
                structuredMemory.ProfileCard,
                structuredMemory.ProjectCards),
            AddedDocuments = nextDocuments.Select(MapDocument).ToArray()
        };
    }

    public async Task<HostedKnowledgeBaseUploadResultDto> PasteDocumentAsync(
        DesktopAccountRecord account,
        HostedKnowledgeBaseDocumentPasteRequestDto request,
        CancellationToken cancellationToken)
    {
        EnsureCanManage(account);
        EnsureEmbeddingsConfigured();

        var normalizedSection = NormalizeSection(request.Section);
        var extractedText = NormalizeSourceText(request.Content);
        if (string.IsNullOrWhiteSpace(extractedText))
        {
            throw new BackendValidationException("Pasted content is required.");
        }

        if (extractedText.Length > MaxCharactersPerDocument)
        {
            throw new BackendValidationException(
                $"Pasted content exceeds the {MaxCharactersPerDocument:N0} character extraction limit.");
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
        var existingChunks = _knowledgeBases.ListChunks(knowledgeBase.KnowledgeBaseId)
            .Where(chunk => !IsCardSyncDocumentId(chunk.DocumentId))
            .ToList();
        var title = string.IsNullOrWhiteSpace(request.Title) ? $"Pasted {normalizedSection}" : request.Title.Trim();
        var documentId = $"kb-doc-{Guid.NewGuid():N}";
        var chunks = BuildChunks(knowledgeBase, account, documentId, title, "text", extractedText, now);
        if (chunks.Count > MaxChunksPerDocument)
        {
            throw new BackendValidationException(
                $"The pasted content exceeds the per-document chunk limit of {MaxChunksPerDocument}.");
        }

        var document = new HostedKnowledgeBaseDocumentRecord
        {
            DocumentId = documentId,
            KnowledgeBaseId = knowledgeBase.KnowledgeBaseId,
            UserId = account.UserId,
            FileName = $"{title}.txt",
            ContentType = "text/plain",
            SourceType = "text",
            Section = normalizedSection,
            SourceKind = "paste",
            SourceLabel = title,
            ExtractedText = extractedText,
            ContentSha256 = ComputeSha256(extractedText),
            CharacterCount = extractedText.Length,
            ChunkCount = chunks.Count,
            Status = "processing",
            Error = string.Empty,
            UploadedAtUtc = now
        };

        var mergedDocuments = existingDocuments.Append(document).ToList();
        var mergedChunks = existingChunks.Concat(chunks).ToList();
        EnsureKnowledgeBaseLimits(mergedDocuments, mergedChunks);

        await IndexChunksAsync(new[] { document }, chunks, cancellationToken);

        knowledgeBase.DocumentCount = mergedDocuments.Count;
        knowledgeBase.EmbeddingModel = _embeddingService.ActiveProfile.ModelId;
        knowledgeBase.EmbeddingVersion = _embeddingService.ActiveProfile.Version;
        knowledgeBase.LastProcessedAtUtc = now;
        knowledgeBase.UpdatedAtUtc = now;

        var structuredMemory = await _structuredExtraction.ExtractAsync(account, knowledgeBase, mergedDocuments, cancellationToken);
        var cardSyncChunks = await BuildStructuredCardSyncChunksAsync(
            account,
            knowledgeBase,
            structuredMemory.ProfileCard,
            structuredMemory.ProjectCards,
            cancellationToken);
        var persistedChunks = mergedChunks.Concat(cardSyncChunks).ToList();
        knowledgeBase.ChunkCount = persistedChunks.Count;
        knowledgeBase.Status = persistedChunks.Count > 0 ? "ready" : "empty";
        _knowledgeBases.ReplaceDocumentsAndChunks(
            knowledgeBase,
            mergedDocuments,
            persistedChunks,
            structuredMemory.ProfileCard,
            structuredMemory.ProjectCards);
        InvalidateSearchCache(knowledgeBase.KnowledgeBaseId);

        return new HostedKnowledgeBaseUploadResultDto
        {
            KnowledgeBase = MapSummary(
                account,
                knowledgeBase,
                mergedDocuments,
                structuredMemory.ProfileCard,
                structuredMemory.ProjectCards),
            AddedDocuments = new[] { MapDocument(document) }
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

    public HostedKnowledgeBaseSummaryDto DeleteDocument(
        DesktopAccountRecord account,
        string documentId)
    {
        EnsureCanManage(account);

        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new BackendValidationException("Document ID is required.");
        }

        var knowledgeBase = _knowledgeBases.FindByUserId(account.UserId)
            ?? throw new BackendValidationException("No hosted knowledge base exists for this account.");

        var existingDocuments = _knowledgeBases.ListDocuments(knowledgeBase.KnowledgeBaseId).ToList();
        var document = existingDocuments.FirstOrDefault(item => string.Equals(item.DocumentId, documentId.Trim(), StringComparison.Ordinal))
            ?? throw new BackendValidationException("Hosted knowledge-base document not found.");
        var remainingDocuments = existingDocuments
            .Where(item => !string.Equals(item.DocumentId, document.DocumentId, StringComparison.Ordinal))
            .ToList();
        var remainingChunks = _knowledgeBases.ListChunks(knowledgeBase.KnowledgeBaseId)
            .Where(chunk =>
                !IsCardSyncDocumentId(chunk.DocumentId)
                && !string.Equals(chunk.DocumentId, document.DocumentId, StringComparison.Ordinal))
            .ToList();
        var now = DateTime.UtcNow;
        knowledgeBase.DocumentCount = remainingDocuments.Count;
        knowledgeBase.LastProcessedAtUtc = now;
        knowledgeBase.UpdatedAtUtc = now;
        if (remainingDocuments.Count == 0)
        {
            knowledgeBase.EmbeddingModel = _embeddingService.ActiveProfile.ModelId;
            knowledgeBase.EmbeddingVersion = _embeddingService.ActiveProfile.Version;
        }

        var structuredMemory = _structuredExtraction.ExtractAsync(account, knowledgeBase, remainingDocuments, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var cardSyncChunks = BuildStructuredCardSyncChunksAsync(
                account,
                knowledgeBase,
                structuredMemory.ProfileCard,
                structuredMemory.ProjectCards,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var persistedChunks = remainingChunks.Concat(cardSyncChunks).ToList();
        knowledgeBase.ChunkCount = persistedChunks.Count;
        knowledgeBase.Status = persistedChunks.Count > 0 ? "ready" : "empty";
        _knowledgeBases.ReplaceDocumentsAndChunks(
            knowledgeBase,
            remainingDocuments,
            persistedChunks,
            structuredMemory.ProfileCard,
            structuredMemory.ProjectCards);
        InvalidateSearchCache(knowledgeBase.KnowledgeBaseId);
        return MapSummary(
            account,
            knowledgeBase,
            remainingDocuments,
            structuredMemory.ProfileCard,
            structuredMemory.ProjectCards);
    }

    public async Task<HostedKnowledgeBaseProfileCardDto> UpdateProfileCard(
        DesktopAccountRecord account,
        HostedKnowledgeBaseProfileCardUpdateRequestDto request,
        CancellationToken cancellationToken)
    {
        EnsureCanManage(account);
        EnsureEmbeddingsConfigured();
        var knowledgeBase = _knowledgeBases.FindByUserId(account.UserId)
            ?? throw new BackendValidationException("No hosted knowledge base exists for this account.");
        var existing = _knowledgeBases.FindProfileCard(knowledgeBase.KnowledgeBaseId)
            ?? throw new BackendValidationException("No hosted profile card exists for this account.");
        var existingDocuments = _knowledgeBases.ListDocuments(knowledgeBase.KnowledgeBaseId).ToList();
        var existingChunks = _knowledgeBases.ListChunks(knowledgeBase.KnowledgeBaseId).ToList();
        var projectCards = _knowledgeBases.ListProjectCards(knowledgeBase.KnowledgeBaseId).ToList();
        var now = DateTime.UtcNow;
        existing.FullName = request.FullName?.Trim() ?? string.Empty;
        existing.ResumeText = request.ResumeText?.Trim() ?? string.Empty;
        existing.ShortIntro = request.ShortIntro?.Trim() ?? string.Empty;
        existing.CurrentRole = request.CurrentRole?.Trim() ?? string.Empty;
        existing.YearsOfExperience = Math.Max(0, request.YearsOfExperience);
        existing.StrengthsJson = JsonSerializer.Serialize(request.Strengths ?? Array.Empty<string>());
        existing.SkillsJson = JsonSerializer.Serialize(request.Skills ?? Array.Empty<string>());
        existing.DomainsJson = JsonSerializer.Serialize(request.Domains ?? Array.Empty<string>());
        existing.UpdatedAtUtc = now;
        var retainedChunks = existingChunks
            .Where(chunk => !string.Equals(chunk.DocumentId, BuildProfileCardSyncDocumentId(knowledgeBase), StringComparison.Ordinal))
            .ToList();
        var syncChunks = await BuildProfileCardSyncChunksAsync(account, knowledgeBase, existing, cancellationToken);
        var persistedChunks = retainedChunks.Concat(syncChunks).ToList();
        knowledgeBase.ChunkCount = persistedChunks.Count;
        knowledgeBase.Status = persistedChunks.Count > 0 ? "ready" : "empty";
        knowledgeBase.EmbeddingModel = _embeddingService.ActiveProfile.ModelId;
        knowledgeBase.EmbeddingVersion = _embeddingService.ActiveProfile.Version;
        knowledgeBase.LastProcessedAtUtc = now;
        knowledgeBase.UpdatedAtUtc = now;
        _knowledgeBases.ReplaceDocumentsAndChunks(
            knowledgeBase,
            existingDocuments,
            persistedChunks,
            existing,
            projectCards);
        InvalidateSearchCache(knowledgeBase.KnowledgeBaseId);
        return MapProfileCard(existing);
    }

    public async Task<HostedKnowledgeBaseProjectCardDto> UpdateProjectCard(
        DesktopAccountRecord account,
        string projectCardId,
        HostedKnowledgeBaseProjectCardUpdateRequestDto request,
        CancellationToken cancellationToken)
    {
        EnsureCanManage(account);
        EnsureEmbeddingsConfigured();
        if (string.IsNullOrWhiteSpace(projectCardId))
        {
            throw new BackendValidationException("Project card ID is required.");
        }

        var knowledgeBase = _knowledgeBases.FindByUserId(account.UserId)
            ?? throw new BackendValidationException("No hosted knowledge base exists for this account.");
        var existing = _knowledgeBases.FindProjectCard(knowledgeBase.KnowledgeBaseId, projectCardId.Trim())
            ?? throw new BackendValidationException("Hosted knowledge-base project card not found.");
        var existingDocuments = _knowledgeBases.ListDocuments(knowledgeBase.KnowledgeBaseId).ToList();
        var existingChunks = _knowledgeBases.ListChunks(knowledgeBase.KnowledgeBaseId).ToList();
        var profileCard = _knowledgeBases.FindProfileCard(knowledgeBase.KnowledgeBaseId);
        var projectCards = _knowledgeBases.ListProjectCards(knowledgeBase.KnowledgeBaseId).ToList();
        var now = DateTime.UtcNow;
        var title = request.Title?.Trim() ?? string.Empty;
        existing.Title = title;
        existing.Slug = Slugify(title);
        existing.IsRecent = request.IsRecent;
        existing.SortOrder = Math.Max(0, request.SortOrder);
        existing.Role = request.Role?.Trim() ?? string.Empty;
        existing.Summary = request.Summary?.Trim() ?? string.Empty;
        existing.StackJson = JsonSerializer.Serialize(request.Stack ?? Array.Empty<string>());
        existing.Architecture = request.Architecture?.Trim() ?? string.Empty;
        existing.Challenges = request.Challenges?.Trim() ?? string.Empty;
        existing.Impact = request.Impact?.Trim() ?? string.Empty;
        existing.UpdatedAtUtc = now;
        var projectCardIndex = projectCards.FindIndex(card =>
            string.Equals(card.ProjectCardId, existing.ProjectCardId, StringComparison.Ordinal));
        if (projectCardIndex >= 0)
        {
            projectCards[projectCardIndex] = existing;
        }
        var retainedChunks = existingChunks
            .Where(chunk => !string.Equals(chunk.DocumentId, BuildProjectCardSyncDocumentId(existing), StringComparison.Ordinal))
            .ToList();
        var syncChunks = await BuildProjectCardSyncChunksAsync(account, knowledgeBase, existing, cancellationToken);
        var persistedChunks = retainedChunks.Concat(syncChunks).ToList();
        knowledgeBase.ChunkCount = persistedChunks.Count;
        knowledgeBase.Status = persistedChunks.Count > 0 ? "ready" : "empty";
        knowledgeBase.EmbeddingModel = _embeddingService.ActiveProfile.ModelId;
        knowledgeBase.EmbeddingVersion = _embeddingService.ActiveProfile.Version;
        knowledgeBase.LastProcessedAtUtc = now;
        knowledgeBase.UpdatedAtUtc = now;
        _knowledgeBases.ReplaceDocumentsAndChunks(
            knowledgeBase,
            existingDocuments,
            persistedChunks,
            profileCard,
            projectCards);
        InvalidateSearchCache(knowledgeBase.KnowledgeBaseId);
        return MapProjectCard(existing);
    }

    public IReadOnlyList<HostedKnowledgeBaseProjectCardDto> SetRecentProject(
        DesktopAccountRecord account,
        string projectCardId)
    {
        EnsureCanManage(account);
        if (string.IsNullOrWhiteSpace(projectCardId))
        {
            throw new BackendValidationException("Project card ID is required.");
        }

        var knowledgeBase = _knowledgeBases.FindByUserId(account.UserId)
            ?? throw new BackendValidationException("No hosted knowledge base exists for this account.");
        var projectCards = _knowledgeBases.ListProjectCards(knowledgeBase.KnowledgeBaseId).ToList();
        if (projectCards.Count == 0)
        {
            throw new BackendValidationException("No project cards exist for this account.");
        }

        var now = DateTime.UtcNow;
        var matched = false;
        foreach (var card in projectCards)
        {
            card.IsRecent = string.Equals(card.ProjectCardId, projectCardId.Trim(), StringComparison.Ordinal);
            card.UpdatedAtUtc = now;
            matched |= card.IsRecent;
            _knowledgeBases.SaveProjectCard(card);
        }

        if (!matched)
        {
            throw new BackendValidationException("Hosted knowledge-base project card not found.");
        }

        return projectCards
            .OrderByDescending(card => card.IsRecent)
            .ThenBy(card => card.SortOrder)
            .Select(MapProjectCard)
            .ToArray();
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
        IReadOnlyList<string>? preferredDocumentIds,
        int maxSnippets,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
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
        var normalizedPreferredDocumentIds = preferredDocumentIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray()
            ?? Array.Empty<string>();
        var snippetLimit = Math.Clamp(maxSnippets, 1, 6);
        var profile = _embeddingService.ActiveProfile;
        var activeEmbeddingProfile = $"{profile.ModelId}:{profile.Version}:{profile.Dimensions}";
        var cacheKey = BuildSearchCacheKey(
            knowledgeBase.KnowledgeBaseId,
            knowledgeBase.LastProcessedAtUtc?.Ticks ?? 0,
            activeEmbeddingProfile,
            normalizedQuery,
            normalizedPreferredDocumentIds,
            snippetLimit);
        if (TryGetCachedSearch(cacheKey, out var cachedSnippets))
        {
            _logger.LogInformation(
                "Hosted KB search cache hit for knowledgeBaseId={KnowledgeBaseId} kbRevision={KnowledgeBaseRevision} mode={SearchMode} queryLength={QueryLength} snippets={SnippetCount} elapsedMs={ElapsedMs}.",
                knowledgeBase.KnowledgeBaseId,
                knowledgeBase.LastProcessedAtUtc?.Ticks ?? 0,
                _embeddingService.IsConfigured ? "hybrid_cache" : "lexical_cache",
                normalizedQuery.Length,
                cachedSnippets.Count,
                stopwatch.ElapsedMilliseconds);
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
                var queryVector = await _embeddingService.GenerateQueryEmbeddingAsync(normalizedQuery, cancellationToken);
                if (queryVector.Length == profile.Dimensions)
                {
                    queryVectorLiteral = ToVectorLiteral(queryVector);
                }
            }
            catch (EmbeddingProviderException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Hosted KB query embedding failed for knowledgeBaseId={KnowledgeBaseId}. Falling back to the non-vector search path.",
                    knowledgeBase.KnowledgeBaseId);
                queryVectorLiteral = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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
        var terms = Tokenize(normalizedQuery).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var candidates = _knowledgeBases.SearchHybridCandidates(
            knowledgeBaseId: knowledgeBase.KnowledgeBaseId,
            query: normalizedQuery,
            preferredDocumentIds: normalizedPreferredDocumentIds,
            restrictToPreferredDocuments: normalizedPreferredDocumentIds.Length > 0,
            queryEmbeddingVector: queryVectorLiteral,
            embeddingModel: profile.ModelId,
            embeddingDimensions: profile.Dimensions,
            embeddingVersion: profile.Version,
            lexicalLimit: candidateLimit,
            semanticLimit: candidateLimit,
            finalLimit: candidateLimit);
        foreach (var (candidate, rank) in candidates.Take(5).Select((candidate, index) => (candidate, index + 1)))
        {
            _logger.LogDebug(
                "Hosted KB candidate kbRevision={KnowledgeBaseRevision} rank={Rank} chunkId={ChunkId} documentId={DocumentId} sectionTitle={SectionTitle} lexicalScore={LexicalScore} semanticScore={SemanticScore} fusedScore={FusedScore}.",
                knowledgeBase.LastProcessedAtUtc?.Ticks ?? 0,
                rank,
                candidate.ChunkId,
                candidate.DocumentId,
                candidate.SectionTitle,
                candidate.LexicalScore,
                candidate.SemanticSimilarity,
                candidate.FusedScore);
        }
        var snippets = BuildSearchSnippets(candidates, terms, snippetLimit, queryVectorLiteral != null);

        if (queryVectorLiteral != null || !_embeddingService.IsConfigured)
        {
            _searchCache[cacheKey] = new CachedSearchEntry
            {
                CachedAtUtc = DateTime.UtcNow,
                Snippets = snippets
            };
            TrimSearchCacheIfNeeded();
        }

        _logger.LogInformation(
            "Hosted KB search completed for knowledgeBaseId={KnowledgeBaseId} kbRevision={KnowledgeBaseRevision} mode={SearchMode} queryLength={QueryLength} candidates={CandidateCount} snippets={SnippetCount} elapsedMs={ElapsedMs}.",
            knowledgeBase.KnowledgeBaseId,
            knowledgeBase.LastProcessedAtUtc?.Ticks ?? 0,
            queryVectorLiteral != null ? "hybrid" : _embeddingService.IsConfigured ? "degraded_lexical" : "lexical",
            normalizedQuery.Length,
            candidates.Count,
            snippets.Count,
            stopwatch.ElapsedMilliseconds);

        return new HostedKnowledgeBaseSearchResultDto
        {
            KnowledgeBase = MapSummary(account, knowledgeBase),
            Snippets = snippets
        };
    }

    private HostedKnowledgeBaseSummaryDto MapSummary(
        DesktopAccountRecord account,
        HostedKnowledgeBaseRecord? knowledgeBase,
        IReadOnlyList<HostedKnowledgeBaseDocumentRecord>? documents = null,
        HostedKnowledgeBaseProfileCardRecord? profileCard = null,
        IReadOnlyList<HostedKnowledgeBaseProjectCardRecord>? projectCards = null)
    {
        var canManage = HasPremiumKnowledgeBaseEntitlement(account, out var blockedReason);
        var documentList = documents
            ?? (knowledgeBase == null ? Array.Empty<HostedKnowledgeBaseDocumentRecord>() : _knowledgeBases.ListDocuments(knowledgeBase.KnowledgeBaseId));
        var profile = profileCard
            ?? (knowledgeBase == null ? null : _knowledgeBases.FindProfileCard(knowledgeBase.KnowledgeBaseId));
        var projects = projectCards
            ?? (knowledgeBase == null ? Array.Empty<HostedKnowledgeBaseProjectCardRecord>() : _knowledgeBases.ListProjectCards(knowledgeBase.KnowledgeBaseId));

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
                ProfileCard = new HostedKnowledgeBaseProfileCardDto(),
                ProjectCards = Array.Empty<HostedKnowledgeBaseProjectCardDto>(),
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
            ProfileCard = profile == null ? new HostedKnowledgeBaseProfileCardDto() : MapProfileCard(profile),
            ProjectCards = projects.Select(MapProjectCard).ToArray(),
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
            Section = document.Section,
            SourceKind = document.SourceKind,
            SourceLabel = document.SourceLabel,
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

    private static HostedKnowledgeBaseProfileCardDto MapProfileCard(HostedKnowledgeBaseProfileCardRecord card)
    {
        return new HostedKnowledgeBaseProfileCardDto
        {
            ProfileCardId = card.ProfileCardId,
            FullName = card.FullName,
            ResumeText = card.ResumeText,
            ShortIntro = card.ShortIntro,
            CurrentRole = card.CurrentRole,
            YearsOfExperience = card.YearsOfExperience,
            Strengths = DeserializeStringList(card.StrengthsJson),
            Skills = DeserializeStringList(card.SkillsJson),
            Domains = DeserializeStringList(card.DomainsJson),
            SourceDocumentIds = DeserializeStringList(card.SourceDocumentIdsJson),
            UpdatedAtUtc = card.UpdatedAtUtc
        };
    }

    private static HostedKnowledgeBaseProjectCardDto MapProjectCard(HostedKnowledgeBaseProjectCardRecord card)
    {
        return new HostedKnowledgeBaseProjectCardDto
        {
            ProjectCardId = card.ProjectCardId,
            Title = card.Title,
            Slug = card.Slug,
            IsRecent = card.IsRecent,
            SortOrder = card.SortOrder,
            Role = card.Role,
            Summary = card.Summary,
            Stack = DeserializeStringList(card.StackJson),
            Architecture = card.Architecture,
            Challenges = card.Challenges,
            Impact = card.Impact,
            SourceDocumentIds = DeserializeStringList(card.SourceDocumentIdsJson),
            UpdatedAtUtc = card.UpdatedAtUtc
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

    private static string NormalizeSection(string? section)
    {
        var normalized = (section ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            HostedKnowledgeBaseStructuredExtractionService.ProfileSection => HostedKnowledgeBaseStructuredExtractionService.ProfileSection,
            HostedKnowledgeBaseStructuredExtractionService.ProjectSection => HostedKnowledgeBaseStructuredExtractionService.ProjectSection,
            _ => HostedKnowledgeBaseStructuredExtractionService.GeneralReferenceSection
        };
    }

    private static IReadOnlyList<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string Slugify(string value)
    {
        var normalized = Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "project" : normalized;
    }

    private static bool IsCardSyncDocumentId(string? documentId)
    {
        return !string.IsNullOrWhiteSpace(documentId)
            && documentId.StartsWith(CardSyncDocumentIdPrefix, StringComparison.Ordinal);
    }

    private static string BuildProfileCardSyncDocumentId(HostedKnowledgeBaseRecord knowledgeBase)
    {
        return $"{CardSyncDocumentIdPrefix}profile-{knowledgeBase.KnowledgeBaseId}";
    }

    private static string BuildProjectCardSyncDocumentId(HostedKnowledgeBaseProjectCardRecord projectCard)
    {
        return $"{CardSyncDocumentIdPrefix}project-{projectCard.ProjectCardId}";
    }

    private static string ResolveDocumentTitle(HostedKnowledgeBaseDocumentRecord document)
    {
        if (!string.IsNullOrWhiteSpace(document.SourceLabel))
        {
            return document.SourceLabel.Trim();
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(document.FileName ?? string.Empty);
        return string.IsNullOrWhiteSpace(fileNameWithoutExtension)
            ? (string.IsNullOrWhiteSpace(document.FileName) ? "Document" : document.FileName.Trim())
            : fileNameWithoutExtension.Trim();
    }

    private static IReadOnlyList<string> MergeSourceDocumentIds(string? existingJson, string? syntheticDocumentId)
    {
        var merged = DeserializeStringList(existingJson)
            .Where(id => !string.IsNullOrWhiteSpace(id) && !IsCardSyncDocumentId(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (!string.IsNullOrWhiteSpace(syntheticDocumentId))
        {
            merged.Add(syntheticDocumentId);
        }

        return merged
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string BuildProfileCardSyncText(HostedKnowledgeBaseProfileCardRecord profileCard)
    {
        var lines = new List<string>
        {
            "Candidate profile"
        };
        AppendStructuredLine(lines, "Full name", profileCard.FullName);
        AppendStructuredLine(lines, "Short intro", profileCard.ShortIntro);
        AppendStructuredLine(lines, "Current role", profileCard.CurrentRole);
        if (profileCard.YearsOfExperience > 0)
        {
            lines.Add($"Years of experience: {profileCard.YearsOfExperience}");
        }

        AppendStructuredList(lines, "Strengths", DeserializeStringList(profileCard.StrengthsJson));
        AppendStructuredList(lines, "Skills", DeserializeStringList(profileCard.SkillsJson));
        AppendStructuredList(lines, "Domains", DeserializeStringList(profileCard.DomainsJson));
        AppendStructuredLine(lines, "Resume details", profileCard.ResumeText);
        return string.Join("\n\n", lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string BuildProjectCardSyncText(HostedKnowledgeBaseProjectCardRecord projectCard)
    {
        var lines = new List<string>
        {
            "Project profile"
        };
        AppendStructuredLine(lines, "Project", projectCard.Title);
        AppendStructuredLine(lines, "Role", projectCard.Role);
        AppendStructuredLine(lines, "Summary", projectCard.Summary);
        AppendStructuredList(lines, "Tech stack", DeserializeStringList(projectCard.StackJson));
        AppendStructuredLine(lines, "Architecture", projectCard.Architecture);
        AppendStructuredLine(lines, "Challenges", projectCard.Challenges);
        AppendStructuredLine(lines, "Impact", projectCard.Impact);
        return string.Join("\n\n", lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static void AppendStructuredLine(ICollection<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {NormalizeSourceText(value)}");
        }
    }

    private static void AppendStructuredList(ICollection<string> lines, string label, IReadOnlyList<string> values)
    {
        var cleaned = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray();
        if (cleaned.Length > 0)
        {
            lines.Add($"{label}: {string.Join(", ", cleaned)}");
        }
    }

    private async Task<IReadOnlyList<HostedKnowledgeBaseChunkRecord>> BuildStructuredCardSyncChunksAsync(
        DesktopAccountRecord account,
        HostedKnowledgeBaseRecord knowledgeBase,
        HostedKnowledgeBaseProfileCardRecord? profileCard,
        IReadOnlyList<HostedKnowledgeBaseProjectCardRecord> projectCards,
        CancellationToken cancellationToken)
    {
        var chunks = new List<HostedKnowledgeBaseChunkRecord>();
        if (profileCard != null)
        {
            chunks.AddRange(await BuildProfileCardSyncChunksAsync(account, knowledgeBase, profileCard, cancellationToken));
        }

        foreach (var projectCard in projectCards)
        {
            chunks.AddRange(await BuildProjectCardSyncChunksAsync(account, knowledgeBase, projectCard, cancellationToken));
        }

        return chunks;
    }

    private async Task<IReadOnlyList<HostedKnowledgeBaseChunkRecord>> BuildProfileCardSyncChunksAsync(
        DesktopAccountRecord account,
        HostedKnowledgeBaseRecord knowledgeBase,
        HostedKnowledgeBaseProfileCardRecord profileCard,
        CancellationToken cancellationToken)
    {
        var documentId = BuildProfileCardSyncDocumentId(knowledgeBase);
        var chunkText = NormalizeSourceText(BuildProfileCardSyncText(profileCard));
        var chunks = string.IsNullOrWhiteSpace(chunkText)
            ? new List<HostedKnowledgeBaseChunkRecord>()
            : BuildChunks(knowledgeBase, account, documentId, "Candidate Profile", "card_profile", chunkText, DateTime.UtcNow);
        profileCard.SourceDocumentIdsJson = JsonSerializer.Serialize(MergeSourceDocumentIds(
            profileCard.SourceDocumentIdsJson,
            chunks.Count > 0 ? documentId : null));
        await IndexChunksAsync(Array.Empty<HostedKnowledgeBaseDocumentRecord>(), chunks, cancellationToken);
        return chunks;
    }

    private async Task<IReadOnlyList<HostedKnowledgeBaseChunkRecord>> BuildProjectCardSyncChunksAsync(
        DesktopAccountRecord account,
        HostedKnowledgeBaseRecord knowledgeBase,
        HostedKnowledgeBaseProjectCardRecord projectCard,
        CancellationToken cancellationToken)
    {
        var documentId = BuildProjectCardSyncDocumentId(projectCard);
        var documentTitle = string.IsNullOrWhiteSpace(projectCard.Title) ? "Project" : projectCard.Title.Trim();
        var chunkText = NormalizeSourceText(BuildProjectCardSyncText(projectCard));
        var chunks = string.IsNullOrWhiteSpace(chunkText)
            ? new List<HostedKnowledgeBaseChunkRecord>()
            : BuildChunks(knowledgeBase, account, documentId, documentTitle, "card_project", chunkText, DateTime.UtcNow);
        projectCard.SourceDocumentIdsJson = JsonSerializer.Serialize(MergeSourceDocumentIds(
            projectCard.SourceDocumentIdsJson,
            chunks.Count > 0 ? documentId : null));
        await IndexChunksAsync(Array.Empty<HostedKnowledgeBaseDocumentRecord>(), chunks, cancellationToken);
        return chunks;
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
        var now = DateTime.UtcNow;
        if (chunks.Count > 0)
        {
            var embeddings = await _embeddingService.GenerateEmbeddingsAsync(chunks.Select(chunk => chunk.Text).ToArray(), cancellationToken);
            if (embeddings.Count != chunks.Count)
            {
                throw new BackendValidationException("Knowledge-base embedding count mismatch during indexing.");
            }

            for (var index = 0; index < chunks.Count; index++)
            {
                chunks[index].EmbeddingVector = ToVectorLiteral(embeddings[index]);
                chunks[index].EmbeddingModel = profile.ModelId;
                chunks[index].EmbeddingVersion = profile.Version;
                chunks[index].IndexedAtUtc = now;
            }
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
                ResolveDocumentTitle(document),
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
                Section = document.Section,
                SourceKind = document.SourceKind,
                SourceLabel = document.SourceLabel,
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
        knowledgeBase.EmbeddingModel = _embeddingService.ActiveProfile.ModelId;
        knowledgeBase.EmbeddingVersion = _embeddingService.ActiveProfile.Version;
        knowledgeBase.LastProcessedAtUtc = now;
        knowledgeBase.UpdatedAtUtc = now;

        var structuredMemory = await _structuredExtraction.ExtractAsync(account, knowledgeBase, rebuiltDocuments, cancellationToken);
        var cardSyncChunks = await BuildStructuredCardSyncChunksAsync(
            account,
            knowledgeBase,
            structuredMemory.ProfileCard,
            structuredMemory.ProjectCards,
            cancellationToken);
        var persistedChunks = rebuiltChunks.Concat(cardSyncChunks).ToList();
        knowledgeBase.ChunkCount = persistedChunks.Count;
        knowledgeBase.Status = persistedChunks.Count > 0 ? "ready" : "empty";
        _knowledgeBases.ReplaceDocumentsAndChunks(
            knowledgeBase,
            rebuiltDocuments,
            persistedChunks,
            structuredMemory.ProfileCard,
            structuredMemory.ProjectCards);
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
            ".pdf" => (ExtractPdf(sourceStream, cancellationToken), "pdf"),
            _ => throw new BackendValidationException($"'{file.FileName}' uses an unsupported document type. Supported types: txt, md, json, csv, log, docx, pdf.")
        };
    }

    private static string ExtractPdf(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            using var document = PdfDocument.Open(stream);
            var pages = new List<string>(document.NumberOfPages);
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = ContentOrderTextExtractor.GetText(page).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    pages.Add($"--- Page {page.Number} ---\n{text}");
                }
            }

            if (pages.Count == 0)
            {
                throw new BackendValidationException("The PDF contains no extractable text. It may be scanned; OCR is required before upload.");
            }

            return string.Join("\n\n", pages);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BackendValidationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new BackendValidationException("The PDF could not be read. Verify that it is a valid, unencrypted PDF.");
        }
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
            + (candidate.SemanticSimilarity * 0.50d)
            + (candidate.LexicalScore * 0.05d)
            + (keywordHits * 0.10d)
            + sectionBoost;
    }

    private static IReadOnlyList<HostedKnowledgeBaseSnippetDto> BuildSearchSnippets(
        IReadOnlyList<HostedKnowledgeBaseSearchCandidateRecord> candidates,
        string[] terms,
        int snippetLimit,
        bool usedSemanticSearch)
    {
        var seenTextFingerprints = new HashSet<string>(StringComparer.Ordinal);
        var snippetsPerDocument = new Dictionary<string, int>(StringComparer.Ordinal);
        var snippets = new List<HostedKnowledgeBaseSnippetDto>(snippetLimit);

        foreach (var candidate in candidates
                     .Select(item => new
                     {
                         Candidate = item,
                         Score = ScoreCandidate(item, terms)
                     })
                     .Where(item => item.Score > 0d)
                     .OrderByDescending(item => item.Score))
        {
            if (candidate.Score < MinSnippetScore)
            {
                continue;
            }

            if (usedSemanticSearch
                && candidate.Candidate.SemanticSimilarity > 0d
                && candidate.Candidate.SemanticSimilarity < MinSemanticSimilarity
                && candidate.Score < (MinSnippetScore + 0.08d))
            {
                continue;
            }

            var documentSnippetCount = snippetsPerDocument.TryGetValue(candidate.Candidate.DocumentId, out var count)
                ? count
                : 0;
            if (documentSnippetCount >= MaxSnippetsPerDocument)
            {
                continue;
            }

            var snippetText = TrimSnippetText(candidate.Candidate.Text, terms);
            var fingerprint = snippetText.Length <= 180
                ? snippetText
                : snippetText[..180];
            if (!seenTextFingerprints.Add(fingerprint))
            {
                continue;
            }

            snippets.Add(new HostedKnowledgeBaseSnippetDto
            {
                DocumentId = candidate.Candidate.DocumentId,
                DocumentTitle = candidate.Candidate.DocumentTitle,
                Text = snippetText,
                Score = candidate.Score
            });
            snippetsPerDocument[candidate.Candidate.DocumentId] = documentSnippetCount + 1;

            if (snippets.Count >= snippetLimit)
            {
                break;
            }
        }

        if (snippets.Count > 0 || candidates.Count == 0)
        {
            return snippets;
        }

        // ponytail: broad prompts like "introduce yourself" can have weak term overlap; use the best semantic candidates instead of returning nothing.
        foreach (var candidate in candidates.Take(Math.Min(snippetLimit, FallbackSnippetCount)))
        {
            var snippetText = TrimSnippetText(candidate.Text, terms);
            if (string.IsNullOrWhiteSpace(snippetText))
            {
                continue;
            }

            snippets.Add(new HostedKnowledgeBaseSnippetDto
            {
                DocumentId = candidate.DocumentId,
                DocumentTitle = candidate.DocumentTitle,
                Text = snippetText,
                Score = Math.Max(candidate.FusedScore, candidate.SemanticSimilarity)
            });
        }

        return snippets;
    }

    private static string TrimSnippetText(string text, string[] terms)
    {
        var normalized = NormalizeChunkText(text);
        if (normalized.Length <= MaxSnippetLength)
        {
            return normalized;
        }

        var anchorIndex = -1;
        foreach (var term in terms)
        {
            anchorIndex = normalized.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (anchorIndex >= 0)
            {
                break;
            }
        }

        var start = anchorIndex <= 0
            ? 0
            : Math.Max(0, anchorIndex - (MaxSnippetLength / 4));
        if (start + MaxSnippetLength > normalized.Length)
        {
            start = Math.Max(0, normalized.Length - MaxSnippetLength);
        }

        var rawLength = Math.Min(MaxSnippetLength, normalized.Length - start);
        var end = start + rawLength;
        var slice = normalized.Substring(start, rawLength).Trim();
        if (start > 0)
        {
            slice = $"...{slice}";
        }

        if (end < normalized.Length)
        {
            slice = $"{slice.TrimEnd('.')}...";
        }

        return slice;
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

    private static string BuildSearchCacheKey(
        string knowledgeBaseId,
        long knowledgeBaseRevision,
        string embeddingProfileKey,
        string normalizedQuery,
        IReadOnlyList<string> preferredDocumentIds,
        int maxSnippets)
    {
        var preferredDocKey = preferredDocumentIds.Count == 0
            ? "-"
            : string.Join(",", preferredDocumentIds);
        return $"{knowledgeBaseId}:{knowledgeBaseRevision}:{embeddingProfileKey}:{maxSnippets}:{preferredDocKey}:{normalizedQuery}";
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
