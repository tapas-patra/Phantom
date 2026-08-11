using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Npgsql;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class HostedKnowledgeBaseRepository
{
    private static readonly HashSet<string> LexicalStopWords = new(StringComparer.Ordinal)
    {
        "about", "and", "did", "does", "for", "from", "has", "have", "how", "into",
        "that", "the", "this", "used", "using", "was", "were", "what", "when", "where", "who", "why", "with", "your"
    };
    private readonly PostgresBackendStore _store;

    public HostedKnowledgeBaseRepository(PostgresBackendStore store)
    {
        _store = store;
        RunLexicalQuerySelfCheck();
    }

    public HostedKnowledgeBaseRecord? FindByUserId(string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM hosted_knowledge_bases WHERE user_id = @userId LIMIT 1;";
        command.Parameters.AddWithValue("userId", userId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapKnowledgeBase(reader) : null;
    }

    public IReadOnlyList<HostedKnowledgeBaseDocumentRecord> ListDocuments(string knowledgeBaseId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM hosted_kb_documents
WHERE knowledge_base_id = @knowledgeBaseId
ORDER BY uploaded_at_utc DESC;";
        command.Parameters.AddWithValue("knowledgeBaseId", knowledgeBaseId);
        using var reader = command.ExecuteReader();
        var items = new List<HostedKnowledgeBaseDocumentRecord>();
        while (reader.Read())
        {
            items.Add(MapDocument(reader));
        }

        return items;
    }

    public HostedKnowledgeBaseDocumentRecord? FindDocument(string knowledgeBaseId, string documentId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM hosted_kb_documents
WHERE knowledge_base_id = @knowledgeBaseId
  AND document_id = @documentId
LIMIT 1;";
        command.Parameters.AddWithValue("knowledgeBaseId", knowledgeBaseId);
        command.Parameters.AddWithValue("documentId", documentId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapDocument(reader) : null;
    }

    public HostedKnowledgeBaseProfileCardRecord? FindProfileCard(string knowledgeBaseId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM hosted_kb_profile_cards
WHERE knowledge_base_id = @knowledgeBaseId
LIMIT 1;";
        command.Parameters.AddWithValue("knowledgeBaseId", knowledgeBaseId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapProfileCard(reader) : null;
    }

    public IReadOnlyList<HostedKnowledgeBaseProjectCardRecord> ListProjectCards(string knowledgeBaseId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM hosted_kb_project_cards
WHERE knowledge_base_id = @knowledgeBaseId
ORDER BY is_recent DESC, sort_order ASC, updated_at_utc DESC;";
        command.Parameters.AddWithValue("knowledgeBaseId", knowledgeBaseId);
        using var reader = command.ExecuteReader();
        var items = new List<HostedKnowledgeBaseProjectCardRecord>();
        while (reader.Read())
        {
            items.Add(MapProjectCard(reader));
        }

        return items;
    }

    public HostedKnowledgeBaseProjectCardRecord? FindProjectCard(string knowledgeBaseId, string projectCardId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM hosted_kb_project_cards
WHERE knowledge_base_id = @knowledgeBaseId
  AND project_card_id = @projectCardId
LIMIT 1;";
        command.Parameters.AddWithValue("knowledgeBaseId", knowledgeBaseId);
        command.Parameters.AddWithValue("projectCardId", projectCardId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapProjectCard(reader) : null;
    }

    public IReadOnlyList<HostedKnowledgeBaseExperienceCardRecord> ListExperienceCards(string knowledgeBaseId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM hosted_kb_experience_cards
WHERE knowledge_base_id = @knowledgeBaseId
ORDER BY is_current DESC, sort_order ASC, updated_at_utc DESC;";
        command.Parameters.AddWithValue("knowledgeBaseId", knowledgeBaseId);
        using var reader = command.ExecuteReader();
        var items = new List<HostedKnowledgeBaseExperienceCardRecord>();
        while (reader.Read())
        {
            items.Add(MapExperienceCard(reader));
        }

        return items;
    }

    public HostedKnowledgeBaseExperienceCardRecord? FindExperienceCard(string knowledgeBaseId, string experienceCardId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM hosted_kb_experience_cards
WHERE knowledge_base_id = @knowledgeBaseId
  AND experience_card_id = @experienceCardId
LIMIT 1;";
        command.Parameters.AddWithValue("knowledgeBaseId", knowledgeBaseId);
        command.Parameters.AddWithValue("experienceCardId", experienceCardId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapExperienceCard(reader) : null;
    }

    public IReadOnlyList<HostedKnowledgeBaseChunkRecord> ListChunks(string knowledgeBaseId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    *,
    CASE
        WHEN embedding IS NULL THEN ''
        ELSE embedding::text
    END AS embedding_vector_text
FROM hosted_kb_chunks
WHERE knowledge_base_id = @knowledgeBaseId
ORDER BY document_id ASC, chunk_index ASC;";
        command.Parameters.AddWithValue("knowledgeBaseId", knowledgeBaseId);
        using var reader = command.ExecuteReader();
        var items = new List<HostedKnowledgeBaseChunkRecord>();
        while (reader.Read())
        {
            items.Add(MapChunk(reader));
        }

        return items;
    }

    // ponytail: soft doc boost only; never hard-pin previous docs, because topic switches are real.
    public IReadOnlyList<HostedKnowledgeBaseSearchCandidateRecord> SearchHybridCandidates(
        string userId,
        string knowledgeBaseId,
        string query,
        IReadOnlyList<string>? preferredDocumentIds,
        bool restrictToPreferredDocuments,
        string? queryEmbeddingVector,
        string embeddingModel,
        int embeddingDimensions,
        int embeddingVersion,
        int lexicalLimit,
        int semanticLimit,
        int finalLimit)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(knowledgeBaseId) || string.IsNullOrWhiteSpace(query) || finalLimit <= 0)
        {
            return Array.Empty<HostedKnowledgeBaseSearchCandidateRecord>();
        }

        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        var semanticDistanceExpression = embeddingDimensions == HostedKnowledgeBaseEmbeddingDefaults.DefaultDimensions
            ? $"CAST(embedding AS vector({HostedKnowledgeBaseEmbeddingDefaults.DefaultDimensions})) <=> CAST(@queryEmbedding AS vector({HostedKnowledgeBaseEmbeddingDefaults.DefaultDimensions}))"
            : "embedding <=> CAST(@queryEmbedding AS vector)";

        if (string.IsNullOrWhiteSpace(queryEmbeddingVector))
        {
            command.CommandText = @"
WITH lexical AS (
    SELECT
        chunk_id,
        document_id,
        document_title,
        section_title,
        text,
        search_text,
        ts_rank_cd(
            to_tsvector('simple', coalesce(document_title, '') || ' ' || search_text),
            websearch_to_tsquery('simple', @lexicalQuery)
        ) AS lexical_score
    FROM hosted_kb_chunks
    WHERE knowledge_base_id = @knowledgeBaseId
      AND EXISTS (
          SELECT 1 FROM hosted_knowledge_bases owner
          WHERE owner.knowledge_base_id = hosted_kb_chunks.knowledge_base_id
            AND owner.user_id = @userId
      )
      AND (NOT @restrictToPreferredDocuments OR document_id = ANY(@preferredDocumentIds))
      AND to_tsvector('simple', coalesce(document_title, '') || ' ' || search_text)
          @@ websearch_to_tsquery('simple', @lexicalQuery)
    ORDER BY lexical_score DESC, document_id ASC, chunk_index ASC
    LIMIT @lexicalLimit
)
SELECT
    chunk_id,
    document_id,
    document_title,
    section_title,
    text,
    search_text,
    lexical_score,
    0::double precision AS semantic_similarity,
    lexical_score
        + CASE
            WHEN @hasPreferredDocuments AND document_id = ANY(@preferredDocumentIds) THEN 0.12
            ELSE 0
        END
        + CASE
            WHEN text LIKE 'Current employment experience%' THEN 0.04
            ELSE 0
        END AS fused_score
FROM lexical
ORDER BY fused_score DESC, lexical_score DESC
LIMIT @finalLimit;";
        }
        else
        {
            command.CommandText = @"
WITH lexical AS (
    SELECT
        chunk_id,
        document_id,
        document_title,
        section_title,
        text,
        search_text,
        ts_rank_cd(
            to_tsvector('simple', coalesce(document_title, '') || ' ' || search_text),
            websearch_to_tsquery('simple', @lexicalQuery)
        ) AS lexical_score,
        row_number() OVER (
            ORDER BY ts_rank_cd(
                to_tsvector('simple', coalesce(document_title, '') || ' ' || search_text),
                websearch_to_tsquery('simple', @lexicalQuery)
            ) DESC,
            document_id ASC,
            chunk_index ASC
        ) AS lexical_rank
    FROM hosted_kb_chunks
    WHERE knowledge_base_id = @knowledgeBaseId
      AND EXISTS (
          SELECT 1 FROM hosted_knowledge_bases owner
          WHERE owner.knowledge_base_id = hosted_kb_chunks.knowledge_base_id
            AND owner.user_id = @userId
      )
      AND (NOT @restrictToPreferredDocuments OR document_id = ANY(@preferredDocumentIds))
      AND to_tsvector('simple', coalesce(document_title, '') || ' ' || search_text)
          @@ websearch_to_tsquery('simple', @lexicalQuery)
    ORDER BY lexical_score DESC, document_id ASC, chunk_index ASC
    LIMIT @lexicalLimit
),
semantic AS (
    SELECT
        chunk_id,
        document_id,
        document_title,
        section_title,
        text,
        search_text,
        1 - (" + semanticDistanceExpression + @") AS semantic_similarity,
        row_number() OVER (
            ORDER BY " + semanticDistanceExpression + @",
            document_id ASC,
            chunk_index ASC
        ) AS semantic_rank
    FROM hosted_kb_chunks
    WHERE knowledge_base_id = @knowledgeBaseId
      AND EXISTS (
          SELECT 1 FROM hosted_knowledge_bases owner
          WHERE owner.knowledge_base_id = hosted_kb_chunks.knowledge_base_id
            AND owner.user_id = @userId
      )
      AND (NOT @restrictToPreferredDocuments OR document_id = ANY(@preferredDocumentIds))
      AND indexed_at_utc IS NOT NULL
      AND embedding IS NOT NULL
      AND embedding_model = @embeddingModel
      AND vector_dims(embedding) = @embeddingDimensions
      AND embedding_version = @embeddingVersion
    ORDER BY " + semanticDistanceExpression + @",
        document_id ASC,
        chunk_index ASC
    LIMIT @semanticLimit
),
combined AS (
    SELECT
        chunk_id,
        document_id,
        document_title,
        section_title,
        text,
        search_text,
        lexical_score,
        0::double precision AS semantic_similarity,
        1.0 / (60 + lexical_rank) AS fused_component
    FROM lexical
    UNION ALL
    SELECT
        chunk_id,
        document_id,
        document_title,
        section_title,
        text,
        search_text,
        0::double precision AS lexical_score,
        semantic_similarity,
        1.0 / (60 + semantic_rank) AS fused_component
    FROM semantic
)
SELECT
    chunk_id,
    document_id,
    document_title,
    section_title,
    text,
    search_text,
    max(lexical_score) AS lexical_score,
    max(semantic_similarity) AS semantic_similarity,
    sum(fused_component)
        + CASE
            WHEN @hasPreferredDocuments AND document_id = ANY(@preferredDocumentIds) THEN 0.12
            ELSE 0
        END
        + CASE
            WHEN text LIKE 'Current employment experience%' THEN 0.04
            ELSE 0
        END AS fused_score
FROM combined
GROUP BY chunk_id, document_id, document_title, section_title, text, search_text
ORDER BY fused_score DESC, semantic_similarity DESC, lexical_score DESC
LIMIT @finalLimit;";
        }

        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("knowledgeBaseId", knowledgeBaseId);
        command.Parameters.AddWithValue(
            "lexicalQuery",
            BuildLexicalQuery(query));
        command.Parameters.AddWithValue("lexicalLimit", lexicalLimit);
        command.Parameters.AddWithValue("semanticLimit", semanticLimit);
        command.Parameters.AddWithValue("finalLimit", finalLimit);
        var preferredDocuments = preferredDocumentIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray()
            ?? Array.Empty<string>();
        command.Parameters.AddWithValue("restrictToPreferredDocuments", restrictToPreferredDocuments);
        command.Parameters.AddWithValue("hasPreferredDocuments", preferredDocuments.Length > 0);
        command.Parameters.AddWithValue("preferredDocumentIds", preferredDocuments);
        if (!string.IsNullOrWhiteSpace(queryEmbeddingVector))
        {
            command.Parameters.AddWithValue("queryEmbedding", queryEmbeddingVector);
            command.Parameters.AddWithValue("embeddingModel", embeddingModel ?? string.Empty);
            command.Parameters.AddWithValue("embeddingDimensions", embeddingDimensions);
            command.Parameters.AddWithValue("embeddingVersion", embeddingVersion);
        }

        using var reader = command.ExecuteReader();
        var items = new List<HostedKnowledgeBaseSearchCandidateRecord>();
        while (reader.Read())
        {
            items.Add(new HostedKnowledgeBaseSearchCandidateRecord
            {
                ChunkId = reader.GetString(reader.GetOrdinal("chunk_id")),
                DocumentId = reader.GetString(reader.GetOrdinal("document_id")),
                DocumentTitle = reader.GetString(reader.GetOrdinal("document_title")),
                SectionTitle = reader.GetString(reader.GetOrdinal("section_title")),
                Text = reader.GetString(reader.GetOrdinal("text")),
                SearchText = reader.GetString(reader.GetOrdinal("search_text")),
                LexicalScore = reader.GetDouble(reader.GetOrdinal("lexical_score")),
                SemanticSimilarity = reader.GetDouble(reader.GetOrdinal("semantic_similarity")),
                FusedScore = reader.GetDouble(reader.GetOrdinal("fused_score"))
            });
        }

        return items;
    }

    private static string BuildLexicalQuery(string query)
        => string.Join(" OR ", Regex.Matches(query.ToLowerInvariant(), "[a-z0-9]+")
            .Select(match => match.Value)
            .Where(term => term.Length >= 3 && !LexicalStopWords.Contains(term))
            .Distinct(StringComparer.Ordinal));

    [Conditional("DEBUG")]
    private static void RunLexicalQuerySelfCheck()
    {
        Debug.Assert(
            BuildLexicalQuery("Why did you use payment retries?") == "use OR payment OR retries",
            "Expanded lexical queries must use safe OR terms.");
    }

    public void SaveKnowledgeBase(HostedKnowledgeBaseRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO hosted_knowledge_bases (
    knowledge_base_id, user_id, name, description, status, embedding_model, embedding_version, document_count, chunk_count, last_processed_at_utc, created_at_utc, updated_at_utc
) VALUES (
    @knowledgeBaseId, @userId, @name, @description, @status, @embeddingModel, @embeddingVersion, @documentCount, @chunkCount, @lastProcessedAtUtc, @createdAtUtc, @updatedAtUtc
)
ON CONFLICT (knowledge_base_id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    status = EXCLUDED.status,
    embedding_model = EXCLUDED.embedding_model,
    embedding_version = EXCLUDED.embedding_version,
    document_count = EXCLUDED.document_count,
    chunk_count = EXCLUDED.chunk_count,
    last_processed_at_utc = EXCLUDED.last_processed_at_utc,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        BindKnowledgeBase(command, record);
        command.ExecuteNonQuery();
    }

    public void ReplaceDocumentsAndChunks(
        HostedKnowledgeBaseRecord knowledgeBase,
        IReadOnlyList<HostedKnowledgeBaseDocumentRecord> documents,
        IReadOnlyList<HostedKnowledgeBaseChunkRecord> chunks,
        HostedKnowledgeBaseProfileCardRecord? profileCard,
        IReadOnlyList<HostedKnowledgeBaseProjectCardRecord> projectCards,
        IReadOnlyList<HostedKnowledgeBaseExperienceCardRecord> experienceCards)
    {
        using var connection = _store.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var deleteChunks = connection.CreateCommand())
        {
            deleteChunks.Transaction = transaction;
            deleteChunks.CommandText = "DELETE FROM hosted_kb_chunks WHERE knowledge_base_id = @knowledgeBaseId;";
            deleteChunks.Parameters.AddWithValue("knowledgeBaseId", knowledgeBase.KnowledgeBaseId);
            deleteChunks.ExecuteNonQuery();
        }

        using (var deleteDocs = connection.CreateCommand())
        {
            deleteDocs.Transaction = transaction;
            deleteDocs.CommandText = "DELETE FROM hosted_kb_documents WHERE knowledge_base_id = @knowledgeBaseId;";
            deleteDocs.Parameters.AddWithValue("knowledgeBaseId", knowledgeBase.KnowledgeBaseId);
            deleteDocs.ExecuteNonQuery();
        }

        using (var deleteProfile = connection.CreateCommand())
        {
            deleteProfile.Transaction = transaction;
            deleteProfile.CommandText = "DELETE FROM hosted_kb_profile_cards WHERE knowledge_base_id = @knowledgeBaseId;";
            deleteProfile.Parameters.AddWithValue("knowledgeBaseId", knowledgeBase.KnowledgeBaseId);
            deleteProfile.ExecuteNonQuery();
        }

        using (var deleteProjects = connection.CreateCommand())
        {
            deleteProjects.Transaction = transaction;
            deleteProjects.CommandText = "DELETE FROM hosted_kb_project_cards WHERE knowledge_base_id = @knowledgeBaseId;";
            deleteProjects.Parameters.AddWithValue("knowledgeBaseId", knowledgeBase.KnowledgeBaseId);
            deleteProjects.ExecuteNonQuery();
        }

        using (var deleteExperiences = connection.CreateCommand())
        {
            deleteExperiences.Transaction = transaction;
            deleteExperiences.CommandText = "DELETE FROM hosted_kb_experience_cards WHERE knowledge_base_id = @knowledgeBaseId;";
            deleteExperiences.Parameters.AddWithValue("knowledgeBaseId", knowledgeBase.KnowledgeBaseId);
            deleteExperiences.ExecuteNonQuery();
        }

        using (var upsertKnowledgeBase = connection.CreateCommand())
        {
            upsertKnowledgeBase.Transaction = transaction;
            upsertKnowledgeBase.CommandText = @"
INSERT INTO hosted_knowledge_bases (
    knowledge_base_id, user_id, name, description, status, embedding_model, embedding_version, document_count, chunk_count, last_processed_at_utc, created_at_utc, updated_at_utc
) VALUES (
    @knowledgeBaseId, @userId, @name, @description, @status, @embeddingModel, @embeddingVersion, @documentCount, @chunkCount, @lastProcessedAtUtc, @createdAtUtc, @updatedAtUtc
)
ON CONFLICT (knowledge_base_id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    status = EXCLUDED.status,
    embedding_model = EXCLUDED.embedding_model,
    embedding_version = EXCLUDED.embedding_version,
    document_count = EXCLUDED.document_count,
    chunk_count = EXCLUDED.chunk_count,
    last_processed_at_utc = EXCLUDED.last_processed_at_utc,
    updated_at_utc = EXCLUDED.updated_at_utc;";
            BindKnowledgeBase(upsertKnowledgeBase, knowledgeBase);
            upsertKnowledgeBase.ExecuteNonQuery();
        }

        foreach (var document in documents)
        {
            using var insertDocument = connection.CreateCommand();
            insertDocument.Transaction = transaction;
            insertDocument.CommandText = @"
INSERT INTO hosted_kb_documents (
    document_id, knowledge_base_id, user_id, file_name, content_type, source_type, section, source_kind, source_label, extracted_text, content_sha256, embedding_model, embedding_version, character_count, chunk_count, status, error, uploaded_at_utc, processed_at_utc, indexed_at_utc
) VALUES (
    @documentId, @knowledgeBaseId, @userId, @fileName, @contentType, @sourceType, @section, @sourceKind, @sourceLabel, @extractedText, @contentSha256, @embeddingModel, @embeddingVersion, @characterCount, @chunkCount, @status, @error, @uploadedAtUtc, @processedAtUtc, @indexedAtUtc
);";
            insertDocument.Parameters.AddWithValue("documentId", document.DocumentId);
            insertDocument.Parameters.AddWithValue("knowledgeBaseId", document.KnowledgeBaseId);
            insertDocument.Parameters.AddWithValue("userId", document.UserId);
            insertDocument.Parameters.AddWithValue("fileName", document.FileName);
            insertDocument.Parameters.AddWithValue("contentType", document.ContentType);
            insertDocument.Parameters.AddWithValue("sourceType", document.SourceType);
            insertDocument.Parameters.AddWithValue("section", document.Section);
            insertDocument.Parameters.AddWithValue("sourceKind", document.SourceKind);
            insertDocument.Parameters.AddWithValue("sourceLabel", document.SourceLabel);
            insertDocument.Parameters.AddWithValue("extractedText", document.ExtractedText);
            insertDocument.Parameters.AddWithValue("contentSha256", document.ContentSha256);
            insertDocument.Parameters.AddWithValue("embeddingModel", document.EmbeddingModel);
            insertDocument.Parameters.AddWithValue("embeddingVersion", document.EmbeddingVersion);
            insertDocument.Parameters.AddWithValue("characterCount", document.CharacterCount);
            insertDocument.Parameters.AddWithValue("chunkCount", document.ChunkCount);
            insertDocument.Parameters.AddWithValue("status", document.Status);
            insertDocument.Parameters.AddWithValue("error", document.Error);
            insertDocument.Parameters.AddWithValue("uploadedAtUtc", document.UploadedAtUtc);
            insertDocument.Parameters.AddWithValue("processedAtUtc", (object?)document.ProcessedAtUtc ?? DBNull.Value);
            insertDocument.Parameters.AddWithValue("indexedAtUtc", (object?)document.IndexedAtUtc ?? DBNull.Value);
            insertDocument.ExecuteNonQuery();
        }

        foreach (var chunk in chunks)
        {
            using var insertChunk = connection.CreateCommand();
            insertChunk.Transaction = transaction;
            insertChunk.CommandText = @"
INSERT INTO hosted_kb_chunks (
    chunk_id, knowledge_base_id, document_id, user_id, chunk_index, document_title, section_title, text, search_text, embedding_json, content_sha256, metadata_json, embedding_model, embedding_version, embedding, token_count, created_at_utc, indexed_at_utc
) VALUES (
    @chunkId, @knowledgeBaseId, @documentId, @userId, @chunkIndex, @documentTitle, @sectionTitle, @text, @searchText, '[]'::jsonb, @contentSha256, CAST(@metadataJson AS jsonb), @embeddingModel, @embeddingVersion, CAST(NULLIF(@embedding, '') AS vector), @tokenCount, @createdAtUtc, @indexedAtUtc
);";
            insertChunk.Parameters.AddWithValue("chunkId", chunk.ChunkId);
            insertChunk.Parameters.AddWithValue("knowledgeBaseId", chunk.KnowledgeBaseId);
            insertChunk.Parameters.AddWithValue("documentId", chunk.DocumentId);
            insertChunk.Parameters.AddWithValue("userId", chunk.UserId);
            insertChunk.Parameters.AddWithValue("chunkIndex", chunk.ChunkIndex);
            insertChunk.Parameters.AddWithValue("documentTitle", chunk.DocumentTitle);
            insertChunk.Parameters.AddWithValue("sectionTitle", chunk.SectionTitle);
            insertChunk.Parameters.AddWithValue("text", chunk.Text);
            insertChunk.Parameters.AddWithValue("searchText", chunk.SearchText);
            insertChunk.Parameters.AddWithValue("contentSha256", chunk.ContentSha256);
            insertChunk.Parameters.AddWithValue("metadataJson", chunk.MetadataJson);
            insertChunk.Parameters.AddWithValue("embeddingModel", chunk.EmbeddingModel);
            insertChunk.Parameters.AddWithValue("embeddingVersion", chunk.EmbeddingVersion);
            insertChunk.Parameters.AddWithValue("embedding", chunk.EmbeddingVector);
            insertChunk.Parameters.AddWithValue("tokenCount", chunk.TokenCount);
            insertChunk.Parameters.AddWithValue("createdAtUtc", chunk.CreatedAtUtc);
            insertChunk.Parameters.AddWithValue("indexedAtUtc", (object?)chunk.IndexedAtUtc ?? DBNull.Value);
            insertChunk.ExecuteNonQuery();
        }

        if (profileCard != null)
        {
            using var insertProfile = connection.CreateCommand();
            insertProfile.Transaction = transaction;
            insertProfile.CommandText = @"
INSERT INTO hosted_kb_profile_cards (
    profile_card_id, knowledge_base_id, user_id, full_name, resume_text, short_intro, current_role_text, years_of_experience, strengths_json, skills_json, domains_json, source_document_ids_json, created_at_utc, updated_at_utc
) VALUES (
    @profileCardId, @knowledgeBaseId, @userId, @fullName, @resumeText, @shortIntro, @currentRole, @yearsOfExperience, CAST(@strengthsJson AS jsonb), CAST(@skillsJson AS jsonb), CAST(@domainsJson AS jsonb), CAST(@sourceDocumentIdsJson AS jsonb), @createdAtUtc, @updatedAtUtc
);";
            BindProfileCard(insertProfile, profileCard);
            insertProfile.ExecuteNonQuery();
        }

        foreach (var projectCard in projectCards)
        {
            using var insertProject = connection.CreateCommand();
            insertProject.Transaction = transaction;
            insertProject.CommandText = @"
INSERT INTO hosted_kb_project_cards (
    project_card_id, knowledge_base_id, user_id, title, slug, is_recent, sort_order, role, summary, stack_json, architecture, challenges, impact, source_document_ids_json, created_at_utc, updated_at_utc
) VALUES (
    @projectCardId, @knowledgeBaseId, @userId, @title, @slug, @isRecent, @sortOrder, @role, @summary, CAST(@stackJson AS jsonb), @architecture, @challenges, @impact, CAST(@sourceDocumentIdsJson AS jsonb), @createdAtUtc, @updatedAtUtc
);";
            BindProjectCard(insertProject, projectCard);
            insertProject.ExecuteNonQuery();
        }

        foreach (var experienceCard in experienceCards)
        {
            using var insertExperience = connection.CreateCommand();
            insertExperience.Transaction = transaction;
            insertExperience.CommandText = ExperienceCardInsertSql;
            BindExperienceCard(insertExperience, experienceCard);
            insertExperience.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void SaveProfileCard(HostedKnowledgeBaseProfileCardRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO hosted_kb_profile_cards (
    profile_card_id, knowledge_base_id, user_id, full_name, resume_text, short_intro, current_role_text, years_of_experience, strengths_json, skills_json, domains_json, source_document_ids_json, created_at_utc, updated_at_utc
) VALUES (
    @profileCardId, @knowledgeBaseId, @userId, @fullName, @resumeText, @shortIntro, @currentRole, @yearsOfExperience, CAST(@strengthsJson AS jsonb), CAST(@skillsJson AS jsonb), CAST(@domainsJson AS jsonb), CAST(@sourceDocumentIdsJson AS jsonb), @createdAtUtc, @updatedAtUtc
)
ON CONFLICT (profile_card_id) DO UPDATE SET
    full_name = EXCLUDED.full_name,
    resume_text = EXCLUDED.resume_text,
    short_intro = EXCLUDED.short_intro,
    current_role_text = EXCLUDED.current_role_text,
    years_of_experience = EXCLUDED.years_of_experience,
    strengths_json = EXCLUDED.strengths_json,
    skills_json = EXCLUDED.skills_json,
    domains_json = EXCLUDED.domains_json,
    source_document_ids_json = EXCLUDED.source_document_ids_json,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        BindProfileCard(command, record);
        command.ExecuteNonQuery();
    }

    public void SaveProjectCard(HostedKnowledgeBaseProjectCardRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO hosted_kb_project_cards (
    project_card_id, knowledge_base_id, user_id, title, slug, is_recent, sort_order, role, summary, stack_json, architecture, challenges, impact, source_document_ids_json, created_at_utc, updated_at_utc
) VALUES (
    @projectCardId, @knowledgeBaseId, @userId, @title, @slug, @isRecent, @sortOrder, @role, @summary, CAST(@stackJson AS jsonb), @architecture, @challenges, @impact, CAST(@sourceDocumentIdsJson AS jsonb), @createdAtUtc, @updatedAtUtc
)
ON CONFLICT (project_card_id) DO UPDATE SET
    title = EXCLUDED.title,
    slug = EXCLUDED.slug,
    is_recent = EXCLUDED.is_recent,
    sort_order = EXCLUDED.sort_order,
    role = EXCLUDED.role,
    summary = EXCLUDED.summary,
    stack_json = EXCLUDED.stack_json,
    architecture = EXCLUDED.architecture,
    challenges = EXCLUDED.challenges,
    impact = EXCLUDED.impact,
    source_document_ids_json = EXCLUDED.source_document_ids_json,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        BindProjectCard(command, record);
        command.ExecuteNonQuery();
    }

    public void SaveExperienceCard(HostedKnowledgeBaseExperienceCardRecord record)
    {
        using var connection = _store.OpenConnection();
        using var transaction = connection.BeginTransaction();
        if (record.IsCurrent)
        {
            using var clearCurrent = connection.CreateCommand();
            clearCurrent.Transaction = transaction;
            clearCurrent.CommandText = @"
UPDATE hosted_kb_experience_cards
SET is_current = FALSE, updated_at_utc = @updatedAtUtc
WHERE knowledge_base_id = @knowledgeBaseId
  AND experience_card_id <> @experienceCardId
  AND is_current;";
            clearCurrent.Parameters.AddWithValue("updatedAtUtc", record.UpdatedAtUtc);
            clearCurrent.Parameters.AddWithValue("knowledgeBaseId", record.KnowledgeBaseId);
            clearCurrent.Parameters.AddWithValue("experienceCardId", record.ExperienceCardId);
            clearCurrent.ExecuteNonQuery();
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ExperienceCardInsertSql + @"
ON CONFLICT (experience_card_id) DO UPDATE SET
    company = EXCLUDED.company,
    role = EXCLUDED.role,
    is_current = EXCLUDED.is_current,
    sort_order = EXCLUDED.sort_order,
    start_date = EXCLUDED.start_date,
    end_date = EXCLUDED.end_date,
    summary = EXCLUDED.summary,
    responsibilities = EXCLUDED.responsibilities,
    skills_json = EXCLUDED.skills_json,
    source_document_ids_json = EXCLUDED.source_document_ids_json,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        BindExperienceCard(command, record);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public void DeleteExperienceCard(string knowledgeBaseId, string experienceCardId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
DELETE FROM hosted_kb_experience_cards
WHERE knowledge_base_id = @knowledgeBaseId
  AND experience_card_id = @experienceCardId;";
        command.Parameters.AddWithValue("knowledgeBaseId", knowledgeBaseId);
        command.Parameters.AddWithValue("experienceCardId", experienceCardId);
        command.ExecuteNonQuery();
    }

    private const string ExperienceCardInsertSql = @"
INSERT INTO hosted_kb_experience_cards (
    experience_card_id, knowledge_base_id, user_id, company, role, is_current, sort_order, start_date, end_date, summary, responsibilities, skills_json, source_document_ids_json, created_at_utc, updated_at_utc
) VALUES (
    @experienceCardId, @knowledgeBaseId, @userId, @company, @role, @isCurrent, @sortOrder, @startDate, @endDate, @summary, @responsibilities, CAST(@skillsJson AS jsonb), CAST(@sourceDocumentIdsJson AS jsonb), @createdAtUtc, @updatedAtUtc
)";

    private static void BindKnowledgeBase(NpgsqlCommand command, HostedKnowledgeBaseRecord record)
    {
        command.Parameters.AddWithValue("knowledgeBaseId", record.KnowledgeBaseId);
        command.Parameters.AddWithValue("userId", record.UserId);
        command.Parameters.AddWithValue("name", record.Name);
        command.Parameters.AddWithValue("description", record.Description);
        command.Parameters.AddWithValue("status", record.Status);
        command.Parameters.AddWithValue("embeddingModel", record.EmbeddingModel);
        command.Parameters.AddWithValue("embeddingVersion", record.EmbeddingVersion);
        command.Parameters.AddWithValue("documentCount", record.DocumentCount);
        command.Parameters.AddWithValue("chunkCount", record.ChunkCount);
        command.Parameters.AddWithValue("lastProcessedAtUtc", (object?)record.LastProcessedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("createdAtUtc", record.CreatedAtUtc);
        command.Parameters.AddWithValue("updatedAtUtc", record.UpdatedAtUtc);
    }

    private static void BindProfileCard(NpgsqlCommand command, HostedKnowledgeBaseProfileCardRecord record)
    {
        command.Parameters.AddWithValue("profileCardId", record.ProfileCardId);
        command.Parameters.AddWithValue("knowledgeBaseId", record.KnowledgeBaseId);
        command.Parameters.AddWithValue("userId", record.UserId);
        command.Parameters.AddWithValue("fullName", record.FullName);
        command.Parameters.AddWithValue("resumeText", record.ResumeText);
        command.Parameters.AddWithValue("shortIntro", record.ShortIntro);
        command.Parameters.AddWithValue("currentRole", record.CurrentRole);
        command.Parameters.AddWithValue("yearsOfExperience", record.YearsOfExperience);
        command.Parameters.AddWithValue("strengthsJson", record.StrengthsJson);
        command.Parameters.AddWithValue("skillsJson", record.SkillsJson);
        command.Parameters.AddWithValue("domainsJson", record.DomainsJson);
        command.Parameters.AddWithValue("sourceDocumentIdsJson", record.SourceDocumentIdsJson);
        command.Parameters.AddWithValue("createdAtUtc", record.CreatedAtUtc);
        command.Parameters.AddWithValue("updatedAtUtc", record.UpdatedAtUtc);
    }

    private static void BindProjectCard(NpgsqlCommand command, HostedKnowledgeBaseProjectCardRecord record)
    {
        command.Parameters.AddWithValue("projectCardId", record.ProjectCardId);
        command.Parameters.AddWithValue("knowledgeBaseId", record.KnowledgeBaseId);
        command.Parameters.AddWithValue("userId", record.UserId);
        command.Parameters.AddWithValue("title", record.Title);
        command.Parameters.AddWithValue("slug", record.Slug);
        command.Parameters.AddWithValue("isRecent", record.IsRecent);
        command.Parameters.AddWithValue("sortOrder", record.SortOrder);
        command.Parameters.AddWithValue("role", record.Role);
        command.Parameters.AddWithValue("summary", record.Summary);
        command.Parameters.AddWithValue("stackJson", record.StackJson);
        command.Parameters.AddWithValue("architecture", record.Architecture);
        command.Parameters.AddWithValue("challenges", record.Challenges);
        command.Parameters.AddWithValue("impact", record.Impact);
        command.Parameters.AddWithValue("sourceDocumentIdsJson", record.SourceDocumentIdsJson);
        command.Parameters.AddWithValue("createdAtUtc", record.CreatedAtUtc);
        command.Parameters.AddWithValue("updatedAtUtc", record.UpdatedAtUtc);
    }

    private static void BindExperienceCard(NpgsqlCommand command, HostedKnowledgeBaseExperienceCardRecord record)
    {
        command.Parameters.AddWithValue("experienceCardId", record.ExperienceCardId);
        command.Parameters.AddWithValue("knowledgeBaseId", record.KnowledgeBaseId);
        command.Parameters.AddWithValue("userId", record.UserId);
        command.Parameters.AddWithValue("company", record.Company);
        command.Parameters.AddWithValue("role", record.Role);
        command.Parameters.AddWithValue("isCurrent", record.IsCurrent);
        command.Parameters.AddWithValue("sortOrder", record.SortOrder);
        command.Parameters.AddWithValue("startDate", record.StartDate);
        command.Parameters.AddWithValue("endDate", record.EndDate);
        command.Parameters.AddWithValue("summary", record.Summary);
        command.Parameters.AddWithValue("responsibilities", record.Responsibilities);
        command.Parameters.AddWithValue("skillsJson", record.SkillsJson);
        command.Parameters.AddWithValue("sourceDocumentIdsJson", record.SourceDocumentIdsJson);
        command.Parameters.AddWithValue("createdAtUtc", record.CreatedAtUtc);
        command.Parameters.AddWithValue("updatedAtUtc", record.UpdatedAtUtc);
    }

    private static HostedKnowledgeBaseRecord MapKnowledgeBase(NpgsqlDataReader reader)
    {
        return new HostedKnowledgeBaseRecord
        {
            KnowledgeBaseId = reader.GetString(reader.GetOrdinal("knowledge_base_id")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            Description = reader.GetString(reader.GetOrdinal("description")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            EmbeddingModel = ReadString(reader, "embedding_model"),
            EmbeddingVersion = ReadInt(reader, "embedding_version"),
            DocumentCount = reader.GetInt32(reader.GetOrdinal("document_count")),
            ChunkCount = reader.GetInt32(reader.GetOrdinal("chunk_count")),
            LastProcessedAtUtc = reader.IsDBNull(reader.GetOrdinal("last_processed_at_utc"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("last_processed_at_utc")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
        };
    }

    private static HostedKnowledgeBaseDocumentRecord MapDocument(NpgsqlDataReader reader)
    {
        return new HostedKnowledgeBaseDocumentRecord
        {
            DocumentId = reader.GetString(reader.GetOrdinal("document_id")),
            KnowledgeBaseId = reader.GetString(reader.GetOrdinal("knowledge_base_id")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            FileName = reader.GetString(reader.GetOrdinal("file_name")),
            ContentType = reader.GetString(reader.GetOrdinal("content_type")),
            SourceType = reader.GetString(reader.GetOrdinal("source_type")),
            Section = ReadString(reader, "section"),
            SourceKind = ReadString(reader, "source_kind"),
            SourceLabel = ReadString(reader, "source_label"),
            ExtractedText = ReadString(reader, "extracted_text"),
            ContentSha256 = ReadString(reader, "content_sha256"),
            EmbeddingModel = ReadString(reader, "embedding_model"),
            EmbeddingVersion = ReadInt(reader, "embedding_version"),
            CharacterCount = reader.GetInt32(reader.GetOrdinal("character_count")),
            ChunkCount = reader.GetInt32(reader.GetOrdinal("chunk_count")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            Error = reader.GetString(reader.GetOrdinal("error")),
            UploadedAtUtc = reader.GetDateTime(reader.GetOrdinal("uploaded_at_utc")),
            ProcessedAtUtc = reader.IsDBNull(reader.GetOrdinal("processed_at_utc"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("processed_at_utc")),
            IndexedAtUtc = ReadDateTime(reader, "indexed_at_utc")
        };
    }

    private static HostedKnowledgeBaseProfileCardRecord MapProfileCard(NpgsqlDataReader reader)
    {
        return new HostedKnowledgeBaseProfileCardRecord
        {
            ProfileCardId = reader.GetString(reader.GetOrdinal("profile_card_id")),
            KnowledgeBaseId = reader.GetString(reader.GetOrdinal("knowledge_base_id")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            FullName = ReadString(reader, "full_name"),
            ResumeText = ReadString(reader, "resume_text"),
            ShortIntro = ReadString(reader, "short_intro"),
            CurrentRole = ReadString(reader, "current_role_text"),
            YearsOfExperience = ReadInt(reader, "years_of_experience"),
            StrengthsJson = ReadString(reader, "strengths_json"),
            SkillsJson = ReadString(reader, "skills_json"),
            DomainsJson = ReadString(reader, "domains_json"),
            SourceDocumentIdsJson = ReadString(reader, "source_document_ids_json"),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
        };
    }

    private static HostedKnowledgeBaseProjectCardRecord MapProjectCard(NpgsqlDataReader reader)
    {
        return new HostedKnowledgeBaseProjectCardRecord
        {
            ProjectCardId = reader.GetString(reader.GetOrdinal("project_card_id")),
            KnowledgeBaseId = reader.GetString(reader.GetOrdinal("knowledge_base_id")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            Title = ReadString(reader, "title"),
            Slug = ReadString(reader, "slug"),
            IsRecent = reader.GetBoolean(reader.GetOrdinal("is_recent")),
            SortOrder = ReadInt(reader, "sort_order"),
            Role = ReadString(reader, "role"),
            Summary = ReadString(reader, "summary"),
            StackJson = ReadString(reader, "stack_json"),
            Architecture = ReadString(reader, "architecture"),
            Challenges = ReadString(reader, "challenges"),
            Impact = ReadString(reader, "impact"),
            SourceDocumentIdsJson = ReadString(reader, "source_document_ids_json"),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
        };
    }

    private static HostedKnowledgeBaseExperienceCardRecord MapExperienceCard(NpgsqlDataReader reader)
    {
        return new HostedKnowledgeBaseExperienceCardRecord
        {
            ExperienceCardId = reader.GetString(reader.GetOrdinal("experience_card_id")),
            KnowledgeBaseId = reader.GetString(reader.GetOrdinal("knowledge_base_id")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            Company = ReadString(reader, "company"),
            Role = ReadString(reader, "role"),
            IsCurrent = reader.GetBoolean(reader.GetOrdinal("is_current")),
            SortOrder = ReadInt(reader, "sort_order"),
            StartDate = ReadString(reader, "start_date"),
            EndDate = ReadString(reader, "end_date"),
            Summary = ReadString(reader, "summary"),
            Responsibilities = ReadString(reader, "responsibilities"),
            SkillsJson = ReadString(reader, "skills_json"),
            SourceDocumentIdsJson = ReadString(reader, "source_document_ids_json"),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
        };
    }

    private static HostedKnowledgeBaseChunkRecord MapChunk(NpgsqlDataReader reader)
    {
        return new HostedKnowledgeBaseChunkRecord
        {
            ChunkId = reader.GetString(reader.GetOrdinal("chunk_id")),
            KnowledgeBaseId = reader.GetString(reader.GetOrdinal("knowledge_base_id")),
            DocumentId = reader.GetString(reader.GetOrdinal("document_id")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            ChunkIndex = reader.GetInt32(reader.GetOrdinal("chunk_index")),
            DocumentTitle = reader.GetString(reader.GetOrdinal("document_title")),
            SectionTitle = ReadString(reader, "section_title"),
            Text = reader.GetString(reader.GetOrdinal("text")),
            SearchText = reader.GetString(reader.GetOrdinal("search_text")),
            EmbeddingVector = ReadString(reader, "embedding_vector_text"),
            ContentSha256 = ReadString(reader, "content_sha256"),
            MetadataJson = ReadString(reader, "metadata_json"),
            EmbeddingModel = ReadString(reader, "embedding_model"),
            EmbeddingVersion = ReadInt(reader, "embedding_version"),
            TokenCount = reader.GetInt32(reader.GetOrdinal("token_count")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            IndexedAtUtc = ReadDateTime(reader, "indexed_at_utc")
        };
    }

    private static string ReadString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static int ReadInt(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
    }

    private static DateTime? ReadDateTime(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }
}
