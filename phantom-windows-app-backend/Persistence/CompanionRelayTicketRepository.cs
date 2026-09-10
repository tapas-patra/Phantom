using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class CompanionRelayTicketRepository
{
    private readonly PostgresBackendStore _store;

    public CompanionRelayTicketRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public CompanionRelayTicketRecord? FindByHash(string ticketHash)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM companion_relay_tickets WHERE ticket_hash = @ticketHash LIMIT 1;";
        command.Parameters.AddWithValue("ticketHash", ticketHash);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Save(CompanionRelayTicketRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO companion_relay_tickets (
    ticket_hash, user_id, pairing_id, role, device_id, expires_at_utc, consumed_at_utc
) VALUES (
    @ticketHash, @userId, @pairingId, @role, @deviceId, @expiresAtUtc, @consumedAtUtc
)
ON CONFLICT(ticket_hash) DO UPDATE SET
    consumed_at_utc = EXCLUDED.consumed_at_utc,
    expires_at_utc = EXCLUDED.expires_at_utc;";
        command.Parameters.AddWithValue("ticketHash", record.TicketHash);
        command.Parameters.AddWithValue("userId", record.UserId);
        command.Parameters.AddWithValue("pairingId", record.PairingId);
        command.Parameters.AddWithValue("role", record.Role);
        command.Parameters.AddWithValue("deviceId", record.DeviceId);
        command.Parameters.AddWithValue("expiresAtUtc", record.ExpiresAtUtc);
        command.Parameters.AddWithValue("consumedAtUtc", (object?)record.ConsumedAtUtc ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void MarkConsumed(string ticketHash, DateTime consumedAtUtc)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE companion_relay_tickets SET consumed_at_utc = @consumedAtUtc WHERE ticket_hash = @ticketHash AND consumed_at_utc IS NULL;";
        command.Parameters.AddWithValue("ticketHash", ticketHash);
        command.Parameters.AddWithValue("consumedAtUtc", consumedAtUtc);
        command.ExecuteNonQuery();
    }

    private static CompanionRelayTicketRecord Map(NpgsqlDataReader reader)
    {
        return new CompanionRelayTicketRecord
        {
            TicketHash = reader.GetString(reader.GetOrdinal("ticket_hash")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            PairingId = reader.GetString(reader.GetOrdinal("pairing_id")),
            Role = reader.GetString(reader.GetOrdinal("role")),
            DeviceId = reader.GetString(reader.GetOrdinal("device_id")),
            ExpiresAtUtc = reader.GetDateTime(reader.GetOrdinal("expires_at_utc")),
            ConsumedAtUtc = reader.IsDBNull(reader.GetOrdinal("consumed_at_utc")) ? null : reader.GetDateTime(reader.GetOrdinal("consumed_at_utc"))
        };
    }
}
