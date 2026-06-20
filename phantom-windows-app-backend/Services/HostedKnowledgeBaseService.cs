using System.IO.Compression;
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
    private const int ChunkSize = 800;
    private const int ChunkOverlap = 120;
    private const int EmbeddingDimensions = 64;

    private readonly HostedKnowledgeBaseRepository _knowledgeBases;
    private readonly AuthSessionRepository _sessions;
    private readonly AccountRepository _accounts;
    private readonly TokenService _tokens;

    public HostedKnowledgeBaseService(
        HostedKnowledgeBaseRepository knowledgeBases,
        AuthSessionRepository sessions,
        AccountRepository accounts,
        TokenService tokens)
    {
        _knowledgeBases = knowledgeBases;
        _sessions = sessions;
        _accounts = accounts;
        _tokens = tokens;
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
            if (string.IsNullOrWhiteSpace(extraction.Text))
            {
                throw new BackendValidationException($"'{file.FileName}' did not produce readable text.");
            }

            if (extraction.Text.Length > MaxCharactersPerDocument)
            {
                throw new BackendValidationException(
                    $"'{file.FileName}' exceeds the {MaxCharactersPerDocument:N0} character extraction limit.");
            }

            var documentId = $"kb-doc-{Guid.NewGuid():N}";
            var documentChunks = BuildChunks(knowledgeBase, account, documentId, file.FileName, extraction.Text, now);
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
                CharacterCount = extraction.Text.Length,
                ChunkCount = documentChunks.Count,
                Status = "ready",
                Error = string.Empty,
                UploadedAtUtc = now,
                ProcessedAtUtc = now
            };

            nextDocuments.Add(document);
            nextChunks.AddRange(documentChunks);
        }

        var mergedDocuments = existingDocuments.Concat(nextDocuments).ToList();
        var mergedChunks = existingChunks.Concat(nextChunks).ToList();

        if (mergedDocuments.Count > MaxDocumentsPerKnowledgeBase)
        {
            throw new BackendValidationException($"Premium knowledge bases support up to {MaxDocumentsPerKnowledgeBase} documents.");
        }

        if (mergedChunks.Count > MaxChunksPerKnowledgeBase)
        {
            throw new BackendValidationException($"The uploaded files exceed the Premium knowledge-base chunk limit of {MaxChunksPerKnowledgeBase}.");
        }

        knowledgeBase.DocumentCount = mergedDocuments.Count;
        knowledgeBase.ChunkCount = mergedChunks.Count;
        knowledgeBase.LastProcessedAtUtc = now;
        knowledgeBase.Status = mergedChunks.Count > 0 ? "ready" : "empty";
        knowledgeBase.UpdatedAtUtc = now;

        _knowledgeBases.ReplaceDocumentsAndChunks(knowledgeBase, mergedDocuments, mergedChunks);

        return new HostedKnowledgeBaseUploadResultDto
        {
            KnowledgeBase = MapSummary(account, knowledgeBase, mergedDocuments),
            AddedDocuments = nextDocuments.Select(MapDocument).ToArray()
        };
    }

    public HostedKnowledgeBaseSearchResultDto Search(DesktopAccountRecord account, string query, int maxSnippets = 3)
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

        var normalizedQuery = NormalizeWhitespace(query);
        var queryEmbedding = BuildEmbedding(normalizedQuery);
        var terms = Tokenize(normalizedQuery).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var topChunks = _knowledgeBases.ListChunks(knowledgeBase.KnowledgeBaseId)
            .Select(chunk => new HostedKnowledgeBaseSnippetDto
            {
                DocumentId = chunk.DocumentId,
                DocumentTitle = chunk.DocumentTitle,
                Text = chunk.Text,
                Score = ScoreChunk(chunk, queryEmbedding, terms)
            })
            .Where(item => item.Score > 0d)
            .OrderByDescending(item => item.Score)
            .Take(Math.Clamp(maxSnippets, 1, 6))
            .ToArray();

        return new HostedKnowledgeBaseSearchResultDto
        {
            KnowledgeBase = MapSummary(account, knowledgeBase),
            Snippets = topChunks
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
            DocumentCount = knowledgeBase.DocumentCount,
            ChunkCount = knowledgeBase.ChunkCount,
            CanManage = canManage,
            CanUseInInterview = canManage && knowledgeBase.ChunkCount > 0,
            BlockedReason = blockedReason,
            LastProcessedAtUtc = knowledgeBase.LastProcessedAtUtc,
            Documents = documentList.Select(MapDocument).ToArray()
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
            CharacterCount = document.CharacterCount,
            ChunkCount = document.ChunkCount,
            Status = document.Status,
            Error = document.Error,
            UploadedAtUtc = document.UploadedAtUtc,
            ProcessedAtUtc = document.ProcessedAtUtc
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
        return NormalizeWhitespace(await reader.ReadToEndAsync(cancellationToken));
    }

    private static async Task<string> ExtractDocxAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new BackendValidationException("The .docx file is missing word/document.xml.");
        await using var entryStream = entry.Open();
        var document = await XDocument.LoadAsync(entryStream, LoadOptions.None, cancellationToken);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var text = string.Join(" ", document.Descendants(w + "t").Select(element => element.Value));
        return NormalizeWhitespace(text);
    }

    private static List<HostedKnowledgeBaseChunkRecord> BuildChunks(
        HostedKnowledgeBaseRecord knowledgeBase,
        DesktopAccountRecord account,
        string documentId,
        string documentTitle,
        string text,
        DateTime createdAtUtc)
    {
        var chunks = new List<HostedKnowledgeBaseChunkRecord>();
        var normalized = NormalizeWhitespace(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return chunks;
        }

        var index = 0;
        for (var start = 0; start < normalized.Length; start += Math.Max(1, ChunkSize - ChunkOverlap))
        {
            var length = Math.Min(ChunkSize, normalized.Length - start);
            var chunkText = normalized.Substring(start, length).Trim();
            if (chunkText.Length == 0)
            {
                continue;
            }

            chunks.Add(new HostedKnowledgeBaseChunkRecord
            {
                ChunkId = $"kb-chunk-{Guid.NewGuid():N}",
                KnowledgeBaseId = knowledgeBase.KnowledgeBaseId,
                DocumentId = documentId,
                UserId = account.UserId,
                ChunkIndex = index++,
                DocumentTitle = documentTitle,
                Text = chunkText,
                SearchText = chunkText.ToLowerInvariant(),
                EmbeddingJson = JsonSerializer.Serialize(BuildEmbedding(chunkText)),
                TokenCount = Tokenize(chunkText).Count,
                CreatedAtUtc = createdAtUtc
            });

            if (start + length >= normalized.Length)
            {
                break;
            }
        }

        return chunks;
    }

    private static double ScoreChunk(HostedKnowledgeBaseChunkRecord chunk, float[] queryEmbedding, string[] terms)
    {
        var chunkEmbedding = JsonSerializer.Deserialize<float[]>(chunk.EmbeddingJson) ?? Array.Empty<float>();
        var cosine = CosineSimilarity(queryEmbedding, chunkEmbedding);
        var keywordHits = terms.Count(term => chunk.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase));
        var keywordScore = terms.Length == 0 ? 0d : (double)keywordHits / terms.Length;
        return cosine + (keywordScore * 0.35d);
    }

    private static float[] BuildEmbedding(string text)
    {
        var vector = new float[EmbeddingDimensions];
        foreach (var token in Tokenize(text))
        {
            var bucket = StableHash(token) % EmbeddingDimensions;
            vector[bucket] += 1f;
        }

        var magnitude = Math.Sqrt(vector.Sum(value => value * value));
        if (magnitude <= 0d)
        {
            return vector;
        }

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(vector[index] / magnitude);
        }

        return vector;
    }

    private static double CosineSimilarity(float[] left, float[] right)
    {
        if (left.Length == 0 || right.Length == 0 || left.Length != right.Length)
        {
            return 0d;
        }

        double dot = 0d;
        double leftMagnitude = 0d;
        double rightMagnitude = 0d;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        if (leftMagnitude <= 0d || rightMagnitude <= 0d)
        {
            return 0d;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static string NormalizeWhitespace(string value)
    {
        return Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261;
            foreach (var character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return (int)(hash & 0x7fffffff);
        }
    }

    private static List<string> Tokenize(string value)
    {
        return Regex.Split(value.ToLowerInvariant(), @"[^a-z0-9+#.]+")
            .Where(token => token.Length >= 3)
            .ToList();
    }
}
