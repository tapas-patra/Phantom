using Npgsql;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class CompanionAuditRepository
{
    private readonly PostgresBackendStore _store;

    public CompanionAuditRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public void Record(string userId, string pairingId, string eventName, string actorRole)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO companion_audit_events (
    event_id, user_id, pairing_id, event_name, actor_role, created_at_utc
) VALUES (
    @eventId, @userId, @pairingId, @eventName, @actorRole, @createdAtUtc
);";
        command.Parameters.AddWithValue("eventId", $"caudit-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("pairingId", string.IsNullOrWhiteSpace(pairingId) ? string.Empty : pairingId);
        command.Parameters.AddWithValue("eventName", eventName);
        command.Parameters.AddWithValue("actorRole", string.IsNullOrWhiteSpace(actorRole) ? string.Empty : actorRole);
        command.Parameters.AddWithValue("createdAtUtc", DateTime.UtcNow);
        command.ExecuteNonQuery();
    }

    public void DeleteOlderThan(TimeSpan age)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM companion_audit_events WHERE created_at_utc < @cutoff;";
        command.Parameters.AddWithValue("cutoff", DateTime.UtcNow - age);
        command.ExecuteNonQuery();
    }
}
