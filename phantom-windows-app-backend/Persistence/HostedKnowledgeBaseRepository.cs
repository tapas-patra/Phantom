using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

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

    public IReadOnlyList<HostedKnowledgeBaseChunkRecord> ListChunks(string knowledgeBaseId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM hosted_kb_chunks
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

    public void SaveKnowledgeBase(HostedKnowledgeBaseRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO hosted_knowledge_bases (
    knowledge_base_id, user_id, name, description, status, document_count, chunk_count, last_processed_at_utc, created_at_utc, updated_at_utc
) VALUES (
    @knowledgeBaseId, @userId, @name, @description, @status, @documentCount, @chunkCount, @lastProcessedAtUtc, @createdAtUtc, @updatedAtUtc
)
ON CONFLICT (knowledge_base_id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    status = EXCLUDED.status,
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
    knowledge_base_id, user_id, name, description, status, document_count, chunk_count, last_processed_at_utc, created_at_utc, updated_at_utc
) VALUES (
    @knowledgeBaseId, @userId, @name, @description, @status, @documentCount, @chunkCount, @lastProcessedAtUtc, @createdAtUtc, @updatedAtUtc
)
ON CONFLICT (knowledge_base_id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    status = EXCLUDED.status,
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
    document_id, knowledge_base_id, user_id, file_name, content_type, source_type, character_count, chunk_count, status, error, uploaded_at_utc, processed_at_utc
) VALUES (
    @documentId, @knowledgeBaseId, @userId, @fileName, @contentType, @sourceType, @characterCount, @chunkCount, @status, @error, @uploadedAtUtc, @processedAtUtc
);";
            insertDocument.Parameters.AddWithValue("documentId", document.DocumentId);
            insertDocument.Parameters.AddWithValue("knowledgeBaseId", document.KnowledgeBaseId);
            insertDocument.Parameters.AddWithValue("userId", document.UserId);
            insertDocument.Parameters.AddWithValue("fileName", document.FileName);
            insertDocument.Parameters.AddWithValue("contentType", document.ContentType);
            insertDocument.Parameters.AddWithValue("sourceType", document.SourceType);
            insertDocument.Parameters.AddWithValue("characterCount", document.CharacterCount);
            insertDocument.Parameters.AddWithValue("chunkCount", document.ChunkCount);
            insertDocument.Parameters.AddWithValue("status", document.Status);
            insertDocument.Parameters.AddWithValue("error", document.Error);
            insertDocument.Parameters.AddWithValue("uploadedAtUtc", document.UploadedAtUtc);
            insertDocument.Parameters.AddWithValue("processedAtUtc", (object?)document.ProcessedAtUtc ?? DBNull.Value);
            insertDocument.ExecuteNonQuery();
        }

        foreach (var chunk in chunks)
        {
            using var insertChunk = connection.CreateCommand();
            insertChunk.Transaction = transaction;
            insertChunk.CommandText = @"
INSERT INTO hosted_kb_chunks (
    chunk_id, knowledge_base_id, document_id, user_id, chunk_index, document_title, text, search_text, embedding_json, token_count, created_at_utc
) VALUES (
    @chunkId, @knowledgeBaseId, @documentId, @userId, @chunkIndex, @documentTitle, @text, @searchText, CAST(@embeddingJson AS jsonb), @tokenCount, @createdAtUtc
);";
            insertChunk.Parameters.AddWithValue("chunkId", chunk.ChunkId);
            insertChunk.Parameters.AddWithValue("knowledgeBaseId", chunk.KnowledgeBaseId);
            insertChunk.Parameters.AddWithValue("documentId", chunk.DocumentId);
            insertChunk.Parameters.AddWithValue("userId", chunk.UserId);
            insertChunk.Parameters.AddWithValue("chunkIndex", chunk.ChunkIndex);
            insertChunk.Parameters.AddWithValue("documentTitle", chunk.DocumentTitle);
            insertChunk.Parameters.AddWithValue("text", chunk.Text);
            insertChunk.Parameters.AddWithValue("searchText", chunk.SearchText);
            insertChunk.Parameters.AddWithValue("embeddingJson", chunk.EmbeddingJson);
            insertChunk.Parameters.AddWithValue("tokenCount", chunk.TokenCount);
            insertChunk.Parameters.AddWithValue("createdAtUtc", chunk.CreatedAtUtc);
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
            CharacterCount = reader.GetInt32(reader.GetOrdinal("character_count")),
            ChunkCount = reader.GetInt32(reader.GetOrdinal("chunk_count")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            Error = reader.GetString(reader.GetOrdinal("error")),
            UploadedAtUtc = reader.GetDateTime(reader.GetOrdinal("uploaded_at_utc")),
            ProcessedAtUtc = reader.IsDBNull(reader.GetOrdinal("processed_at_utc"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("processed_at_utc"))
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
            Text = reader.GetString(reader.GetOrdinal("text")),
            SearchText = reader.GetString(reader.GetOrdinal("search_text")),
            EmbeddingJson = reader.GetString(reader.GetOrdinal("embedding_json")),
            TokenCount = reader.GetInt32(reader.GetOrdinal("token_count")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc"))
        };
    }
}
