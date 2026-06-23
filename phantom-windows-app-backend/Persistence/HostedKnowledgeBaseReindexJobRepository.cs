using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class HostedKnowledgeBaseReindexJobRepository
{
    private readonly PostgresBackendStore _store;

    public HostedKnowledgeBaseReindexJobRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public HostedKnowledgeBaseReindexJobRecord? FindLatestForKnowledgeBase(string knowledgeBaseId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT *
FROM hosted_kb_reindex_jobs
WHERE knowledge_base_id = @knowledgeBaseId
ORDER BY requested_at_utc DESC
LIMIT 1;";
        command.Parameters.AddWithValue("knowledgeBaseId", knowledgeBaseId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public HostedKnowledgeBaseReindexJobRecord? FindByJobIdForUser(string jobId, string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT *
FROM hosted_kb_reindex_jobs
WHERE job_id = @jobId
  AND user_id = @userId
LIMIT 1;";
        command.Parameters.AddWithValue("jobId", jobId);
        command.Parameters.AddWithValue("userId", userId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public HostedKnowledgeBaseReindexJobRecord? FindActiveForKnowledgeBase(string knowledgeBaseId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT *
FROM hosted_kb_reindex_jobs
WHERE knowledge_base_id = @knowledgeBaseId
  AND status IN ('queued', 'running')
ORDER BY requested_at_utc DESC
LIMIT 1;";
        command.Parameters.AddWithValue("knowledgeBaseId", knowledgeBaseId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public bool Enqueue(HostedKnowledgeBaseReindexJobRecord record)
    {
        try
        {
            using var connection = _store.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO hosted_kb_reindex_jobs (
    job_id, knowledge_base_id, user_id, status, error, target_embedding_model, target_embedding_version,
    total_documents, processed_documents, requested_at_utc, started_at_utc, completed_at_utc, updated_at_utc
) VALUES (
    @jobId, @knowledgeBaseId, @userId, @status, @error, @targetEmbeddingModel, @targetEmbeddingVersion,
    @totalDocuments, @processedDocuments, @requestedAtUtc, @startedAtUtc, @completedAtUtc, @updatedAtUtc
);";
            Bind(command, record);
            command.ExecuteNonQuery();
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    public HostedKnowledgeBaseReindexJobRecord? TryStartNextQueued()
    {
        using var connection = _store.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
WITH next_job AS (
    SELECT job_id
    FROM hosted_kb_reindex_jobs
    WHERE status = 'queued'
    ORDER BY requested_at_utc ASC
    LIMIT 1
    FOR UPDATE SKIP LOCKED
)
UPDATE hosted_kb_reindex_jobs jobs
SET status = 'running',
    started_at_utc = NOW(),
    updated_at_utc = NOW()
FROM next_job
WHERE jobs.job_id = next_job.job_id
RETURNING jobs.*;";
        using var reader = command.ExecuteReader();
        HostedKnowledgeBaseReindexJobRecord? record = null;
        if (reader.Read())
        {
            record = Map(reader);
        }

        reader.Close();
        transaction.Commit();
        return record;
    }

    public void RequeueRunningJobs()
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE hosted_kb_reindex_jobs
SET status = 'queued',
    started_at_utc = NULL,
    updated_at_utc = NOW()
WHERE status = 'running';";
        command.ExecuteNonQuery();
    }

    public void UpdateProgress(string jobId, int totalDocuments, int processedDocuments)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE hosted_kb_reindex_jobs
SET total_documents = @totalDocuments,
    processed_documents = @processedDocuments,
    updated_at_utc = NOW()
WHERE job_id = @jobId;";
        command.Parameters.AddWithValue("jobId", jobId);
        command.Parameters.AddWithValue("totalDocuments", totalDocuments);
        command.Parameters.AddWithValue("processedDocuments", processedDocuments);
        command.ExecuteNonQuery();
    }

    public void MarkCompleted(string jobId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE hosted_kb_reindex_jobs
SET status = 'completed',
    error = '',
    processed_documents = total_documents,
    completed_at_utc = NOW(),
    updated_at_utc = NOW()
WHERE job_id = @jobId;";
        command.Parameters.AddWithValue("jobId", jobId);
        command.ExecuteNonQuery();
    }

    public void MarkFailed(string jobId, string error)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE hosted_kb_reindex_jobs
SET status = 'failed',
    error = @error,
    completed_at_utc = NOW(),
    updated_at_utc = NOW()
WHERE job_id = @jobId;";
        command.Parameters.AddWithValue("jobId", jobId);
        command.Parameters.AddWithValue("error", error);
        command.ExecuteNonQuery();
    }

    private static void Bind(NpgsqlCommand command, HostedKnowledgeBaseReindexJobRecord record)
    {
        command.Parameters.AddWithValue("jobId", record.JobId);
        command.Parameters.AddWithValue("knowledgeBaseId", record.KnowledgeBaseId);
        command.Parameters.AddWithValue("userId", record.UserId);
        command.Parameters.AddWithValue("status", record.Status);
        command.Parameters.AddWithValue("error", record.Error);
        command.Parameters.AddWithValue("targetEmbeddingModel", record.TargetEmbeddingModel);
        command.Parameters.AddWithValue("targetEmbeddingVersion", record.TargetEmbeddingVersion);
        command.Parameters.AddWithValue("totalDocuments", record.TotalDocuments);
        command.Parameters.AddWithValue("processedDocuments", record.ProcessedDocuments);
        command.Parameters.AddWithValue("requestedAtUtc", record.RequestedAtUtc);
        command.Parameters.AddWithValue("startedAtUtc", (object?)record.StartedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("completedAtUtc", (object?)record.CompletedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("updatedAtUtc", record.UpdatedAtUtc);
    }

    private static HostedKnowledgeBaseReindexJobRecord Map(NpgsqlDataReader reader)
    {
        return new HostedKnowledgeBaseReindexJobRecord
        {
            JobId = reader.GetString(reader.GetOrdinal("job_id")),
            KnowledgeBaseId = reader.GetString(reader.GetOrdinal("knowledge_base_id")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            Error = reader.GetString(reader.GetOrdinal("error")),
            TargetEmbeddingModel = reader.GetString(reader.GetOrdinal("target_embedding_model")),
            TargetEmbeddingVersion = reader.GetInt32(reader.GetOrdinal("target_embedding_version")),
            TotalDocuments = reader.GetInt32(reader.GetOrdinal("total_documents")),
            ProcessedDocuments = reader.GetInt32(reader.GetOrdinal("processed_documents")),
            RequestedAtUtc = reader.GetDateTime(reader.GetOrdinal("requested_at_utc")),
            StartedAtUtc = ReadDateTime(reader, "started_at_utc"),
            CompletedAtUtc = ReadDateTime(reader, "completed_at_utc"),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
        };
    }

    private static DateTime? ReadDateTime(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }
}
