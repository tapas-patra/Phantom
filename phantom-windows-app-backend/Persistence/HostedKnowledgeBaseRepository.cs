using System;
using System.Collections.Generic;
using System.Linq;
using Npgsql;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class HostedKnowledgeBaseRepository
{
    private readonly PostgresBackendStore _store;

    public HostedKnowledgeBaseRepository(PostgresBackendStore store)
    {
        _store = store;
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
        if (string.IsNullOrWhiteSpace(knowledgeBaseId) || string.IsNullOrWhiteSpace(query) || finalLimit <= 0)
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
            plainto_tsquery('simple', @query)
        ) AS lexical_score
    FROM hosted_kb_chunks
    WHERE knowledge_base_id = @knowledgeBaseId
      AND (NOT @restrictToPreferredDocuments OR document_id = ANY(@preferredDocumentIds))
      AND to_tsvector('simple', coalesce(document_title, '') || ' ' || search_text)
          @@ plainto_tsquery('simple', @query)
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
            plainto_tsquery('simple', @query)
        ) AS lexical_score,
        row_number() OVER (
            ORDER BY ts_rank_cd(
                to_tsvector('simple', coalesce(document_title, '') || ' ' || search_text),
                plainto_tsquery('simple', @query)
            ) DESC,
            document_id ASC,
            chunk_index ASC
        ) AS lexical_rank
    FROM hosted_kb_chunks
    WHERE knowledge_base_id = @knowledgeBaseId
      AND (NOT @restrictToPreferredDocuments OR document_id = ANY(@preferredDocumentIds))
      AND to_tsvector('simple', coalesce(document_title, '') || ' ' || search_text)
          @@ plainto_tsquery('simple', @query)
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
        END AS fused_score
FROM combined
GROUP BY chunk_id, document_id, document_title, section_title, text, search_text
ORDER BY fused_score DESC, semantic_similarity DESC, lexical_score DESC
LIMIT @finalLimit;";
        }

        command.Parameters.AddWithValue("knowledgeBaseId", knowledgeBaseId);
        command.Parameters.AddWithValue("query", query);
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
        IReadOnlyList<HostedKnowledgeBaseChunkRecord> chunks)
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
    document_id, knowledge_base_id, user_id, file_name, content_type, source_type, extracted_text, content_sha256, embedding_model, embedding_version, character_count, chunk_count, status, error, uploaded_at_utc, processed_at_utc, indexed_at_utc
) VALUES (
    @documentId, @knowledgeBaseId, @userId, @fileName, @contentType, @sourceType, @extractedText, @contentSha256, @embeddingModel, @embeddingVersion, @characterCount, @chunkCount, @status, @error, @uploadedAtUtc, @processedAtUtc, @indexedAtUtc
);";
            insertDocument.Parameters.AddWithValue("documentId", document.DocumentId);
            insertDocument.Parameters.AddWithValue("knowledgeBaseId", document.KnowledgeBaseId);
            insertDocument.Parameters.AddWithValue("userId", document.UserId);
            insertDocument.Parameters.AddWithValue("fileName", document.FileName);
            insertDocument.Parameters.AddWithValue("contentType", document.ContentType);
            insertDocument.Parameters.AddWithValue("sourceType", document.SourceType);
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

        transaction.Commit();
    }

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
