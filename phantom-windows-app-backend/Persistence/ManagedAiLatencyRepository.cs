using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class ManagedAiLatencyRepository
{
    private readonly PostgresBackendStore _store;

    public ManagedAiLatencyRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public ManagedAiLatencyRunRecord? FindActiveRun()
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT *
FROM managed_ai_latency_runs
WHERE status IN ('queued', 'running')
ORDER BY requested_at_utc DESC
LIMIT 1;";
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapRun(reader) : null;
    }

    public ManagedAiLatencyRunRecord? GetLatestRun()
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT *
FROM managed_ai_latency_runs
ORDER BY requested_at_utc DESC
LIMIT 1;";
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapRun(reader) : null;
    }

    public bool Enqueue(ManagedAiLatencyRunRecord record)
    {
        try
        {
            using var connection = _store.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO managed_ai_latency_runs (
    job_id, status, total_models, processed_models, error,
    requested_at_utc, started_at_utc, completed_at_utc, updated_at_utc
) VALUES (
    @jobId, @status, @totalModels, @processedModels, @error,
    @requestedAtUtc, @startedAtUtc, @completedAtUtc, @updatedAtUtc
);";
            BindRun(command, record);
            command.ExecuteNonQuery();
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    public ManagedAiLatencyRunRecord? TryStartNextQueued()
    {
        using var connection = _store.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
WITH next_job AS (
    SELECT job_id
    FROM managed_ai_latency_runs
    WHERE status = 'queued'
    ORDER BY requested_at_utc ASC
    LIMIT 1
    FOR UPDATE SKIP LOCKED
)
UPDATE managed_ai_latency_runs runs
SET status = 'running',
    started_at_utc = NOW(),
    updated_at_utc = NOW()
FROM next_job
WHERE runs.job_id = next_job.job_id
RETURNING runs.*;";
        using var reader = command.ExecuteReader();
        ManagedAiLatencyRunRecord? record = null;
        if (reader.Read())
        {
            record = MapRun(reader);
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
UPDATE managed_ai_latency_runs
SET status = 'queued',
    started_at_utc = NULL,
    updated_at_utc = NOW()
WHERE status = 'running';";
        command.ExecuteNonQuery();
    }

    public void UpdateProgress(string jobId, int totalModels, int processedModels)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE managed_ai_latency_runs
SET total_models = @totalModels,
    processed_models = @processedModels,
    updated_at_utc = NOW()
WHERE job_id = @jobId;";
        command.Parameters.AddWithValue("jobId", jobId);
        command.Parameters.AddWithValue("totalModels", totalModels);
        command.Parameters.AddWithValue("processedModels", processedModels);
        command.ExecuteNonQuery();
    }

    public void MarkCompleted(string jobId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE managed_ai_latency_runs
SET status = 'completed',
    error = '',
    processed_models = total_models,
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
UPDATE managed_ai_latency_runs
SET status = 'failed',
    error = @error,
    completed_at_utc = NOW(),
    updated_at_utc = NOW()
WHERE job_id = @jobId;";
        command.Parameters.AddWithValue("jobId", jobId);
        command.Parameters.AddWithValue("error", error);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ManagedAiLatencyModelStatusRecord> ListModelStatuses()
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT *
FROM managed_ai_model_latency_status
ORDER BY provider_id ASC, model_display_name ASC, model_id ASC;";
        using var reader = command.ExecuteReader();
        var items = new List<ManagedAiLatencyModelStatusRecord>();
        while (reader.Read())
        {
            items.Add(MapModelStatus(reader));
        }

        return items;
    }

    public void UpsertModelStatus(ManagedAiLatencyModelStatusRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO managed_ai_model_latency_status (
    provider_id, model_id, model_display_name, supports_vision, is_chat_capable,
    status, message, latency_ms, checked_at_utc, last_job_id, updated_at_utc
) VALUES (
    @providerId, @modelId, @modelDisplayName, @supportsVision, @isChatCapable,
    @status, @message, @latencyMs, @checkedAtUtc, @lastJobId, @updatedAtUtc
)
ON CONFLICT (provider_id, model_id) DO UPDATE SET
    model_display_name = EXCLUDED.model_display_name,
    supports_vision = EXCLUDED.supports_vision,
    is_chat_capable = EXCLUDED.is_chat_capable,
    status = EXCLUDED.status,
    message = EXCLUDED.message,
    latency_ms = EXCLUDED.latency_ms,
    checked_at_utc = EXCLUDED.checked_at_utc,
    last_job_id = EXCLUDED.last_job_id,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        command.Parameters.AddWithValue("providerId", record.ProviderId);
        command.Parameters.AddWithValue("modelId", record.ModelId);
        command.Parameters.AddWithValue("modelDisplayName", record.ModelDisplayName);
        command.Parameters.AddWithValue("supportsVision", record.SupportsVision);
        command.Parameters.AddWithValue("isChatCapable", (object?)record.IsChatCapable ?? DBNull.Value);
        command.Parameters.AddWithValue("status", record.Status);
        command.Parameters.AddWithValue("message", record.Message);
        command.Parameters.AddWithValue("latencyMs", (object?)record.LatencyMs ?? DBNull.Value);
        command.Parameters.AddWithValue("checkedAtUtc", (object?)record.CheckedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("lastJobId", string.IsNullOrWhiteSpace(record.LastJobId) ? DBNull.Value : record.LastJobId);
        command.Parameters.AddWithValue("updatedAtUtc", record.UpdatedAtUtc);
        command.ExecuteNonQuery();
    }

    private static void BindRun(NpgsqlCommand command, ManagedAiLatencyRunRecord record)
    {
        command.Parameters.AddWithValue("jobId", record.JobId);
        command.Parameters.AddWithValue("status", record.Status);
        command.Parameters.AddWithValue("totalModels", record.TotalModels);
        command.Parameters.AddWithValue("processedModels", record.ProcessedModels);
        command.Parameters.AddWithValue("error", record.Error);
        command.Parameters.AddWithValue("requestedAtUtc", record.RequestedAtUtc);
        command.Parameters.AddWithValue("startedAtUtc", (object?)record.StartedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("completedAtUtc", (object?)record.CompletedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("updatedAtUtc", record.UpdatedAtUtc);
    }

    private static ManagedAiLatencyRunRecord MapRun(NpgsqlDataReader reader)
    {
        return new ManagedAiLatencyRunRecord
        {
            JobId = reader.GetString(reader.GetOrdinal("job_id")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            TotalModels = reader.GetInt32(reader.GetOrdinal("total_models")),
            ProcessedModels = reader.GetInt32(reader.GetOrdinal("processed_models")),
            Error = reader.GetString(reader.GetOrdinal("error")),
            RequestedAtUtc = reader.GetDateTime(reader.GetOrdinal("requested_at_utc")),
            StartedAtUtc = ReadDateTime(reader, "started_at_utc"),
            CompletedAtUtc = ReadDateTime(reader, "completed_at_utc"),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
        };
    }

    private static ManagedAiLatencyModelStatusRecord MapModelStatus(NpgsqlDataReader reader)
    {
        return new ManagedAiLatencyModelStatusRecord
        {
            ProviderId = reader.GetString(reader.GetOrdinal("provider_id")),
            ModelId = reader.GetString(reader.GetOrdinal("model_id")),
            ModelDisplayName = reader.GetString(reader.GetOrdinal("model_display_name")),
            SupportsVision = reader.GetBoolean(reader.GetOrdinal("supports_vision")),
            IsChatCapable = ReadBoolean(reader, "is_chat_capable"),
            Status = reader.GetString(reader.GetOrdinal("status")),
            Message = reader.GetString(reader.GetOrdinal("message")),
            LatencyMs = ReadInt(reader, "latency_ms"),
            CheckedAtUtc = ReadDateTime(reader, "checked_at_utc"),
            LastJobId = ReadString(reader, "last_job_id"),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
        };
    }

    private static string ReadString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static bool? ReadBoolean(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
    }

    private static int? ReadInt(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static DateTime? ReadDateTime(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }
}
