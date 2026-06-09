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
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
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
}
