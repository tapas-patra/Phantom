using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class TelemetryRepository
{
    private readonly PostgresBackendStore _store;

    public TelemetryRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public void Save(TelemetryEventRecord record)
    {
        SaveBatch(new[] { record });
    }

    public void SaveBatch(IEnumerable<TelemetryEventRecord> records)
    {
        using var connection = _store.OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var record in records)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO telemetry_events (
    event_id, category, event_name, payload_json, created_at_utc
) VALUES (
    @eventId, @category, @eventName, CAST(@payloadJson AS jsonb), @createdAtUtc
);";
            command.Parameters.AddWithValue("eventId", record.EventId);
            command.Parameters.AddWithValue("category", record.Category);
            command.Parameters.AddWithValue("eventName", record.EventName);
            command.Parameters.AddWithValue("payloadJson", record.PayloadJson);
            command.Parameters.AddWithValue("createdAtUtc", record.CreatedAtUtc);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
