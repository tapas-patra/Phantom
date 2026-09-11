using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class CompanionPairingRepository
{
    private readonly PostgresBackendStore _store;

    public CompanionPairingRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public CompanionPairingRecord? FindById(string pairingId)
    {
        using var connection = _store.OpenConnection();
        return FindById(pairingId, connection, transaction: null, forUpdate: false);
    }

    public CompanionPairingRecord? FindById(
        string pairingId,
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        bool forUpdate)
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"SELECT * FROM companion_pairings WHERE pairing_id = @pairingId LIMIT 1{(forUpdate ? " FOR UPDATE" : string.Empty)};";
        command.Parameters.AddWithValue("pairingId", pairingId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public CompanionPairingRecord? FindActiveByDesktopDevice(string desktopDeviceId)
    {
        using var connection = _store.OpenConnection();
        return FindActiveByDesktopDevice(desktopDeviceId, connection, transaction: null, forUpdate: false);
    }

    public CompanionPairingRecord? FindActiveByDesktopDevice(
        string desktopDeviceId,
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        bool forUpdate)
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText = $@"
SELECT * FROM companion_pairings
WHERE desktop_device_id = @desktopDeviceId
  AND revoked_at_utc IS NULL
  AND companion_device_id <> ''
ORDER BY created_at_utc DESC
LIMIT 1{(forUpdate ? " FOR UPDATE" : string.Empty)};";
        command.Parameters.AddWithValue("desktopDeviceId", desktopDeviceId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public CompanionPairingRecord? FindActiveByCompanionDevice(string companionDeviceId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM companion_pairings
WHERE companion_device_id = @companionDeviceId
  AND revoked_at_utc IS NULL
ORDER BY created_at_utc DESC
LIMIT 1;";
        command.Parameters.AddWithValue("companionDeviceId", companionDeviceId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<CompanionPairingRecord> ListByUser(string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM companion_pairings
WHERE user_id = @userId
ORDER BY created_at_utc DESC;";
        command.Parameters.AddWithValue("userId", userId);
        using var reader = command.ExecuteReader();
        var items = new List<CompanionPairingRecord>();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }
        return items;
    }

    public void Save(CompanionPairingRecord record)
    {
        using var connection = _store.OpenConnection();
        Save(record, connection, transaction: null);
    }

    public void Save(CompanionPairingRecord record, NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText = @"
INSERT INTO companion_pairings (
    pairing_id, user_id, desktop_device_id, desktop_device_label, desktop_platform,
    companion_device_id, companion_device_label, companion_platform, status,
    created_at_utc, paired_at_utc, revoked_at_utc
) VALUES (
    @pairingId, @userId, @desktopDeviceId, @desktopDeviceLabel, @desktopPlatform,
    @companionDeviceId, @companionDeviceLabel, @companionPlatform, @status,
    @createdAtUtc, @pairedAtUtc, @revokedAtUtc
)
ON CONFLICT(pairing_id) DO UPDATE SET
    companion_device_id = EXCLUDED.companion_device_id,
    companion_device_label = EXCLUDED.companion_device_label,
    companion_platform = EXCLUDED.companion_platform,
    status = EXCLUDED.status,
    paired_at_utc = EXCLUDED.paired_at_utc,
    revoked_at_utc = EXCLUDED.revoked_at_utc;";
        command.Parameters.AddWithValue("pairingId", record.PairingId);
        command.Parameters.AddWithValue("userId", record.UserId);
        command.Parameters.AddWithValue("desktopDeviceId", record.DesktopDeviceId);
        command.Parameters.AddWithValue("desktopDeviceLabel", record.DesktopDeviceLabel);
        command.Parameters.AddWithValue("desktopPlatform", record.DesktopPlatform);
        command.Parameters.AddWithValue("companionDeviceId", record.CompanionDeviceId);
        command.Parameters.AddWithValue("companionDeviceLabel", record.CompanionDeviceLabel);
        command.Parameters.AddWithValue("companionPlatform", record.CompanionPlatform);
        command.Parameters.AddWithValue("status", record.Status);
        command.Parameters.AddWithValue("createdAtUtc", record.CreatedAtUtc);
        command.Parameters.AddWithValue("pairedAtUtc", (object?)record.PairedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("revokedAtUtc", (object?)record.RevokedAtUtc ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void Revoke(string pairingId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE companion_pairings
SET revoked_at_utc = @revokedAtUtc, status = 'revoked'
WHERE pairing_id = @pairingId AND revoked_at_utc IS NULL;";
        command.Parameters.AddWithValue("pairingId", pairingId);
        command.Parameters.AddWithValue("revokedAtUtc", DateTime.UtcNow);
        command.ExecuteNonQuery();
    }

    private static NpgsqlCommand CreateCommand(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        return command;
    }

    private static CompanionPairingRecord Map(NpgsqlDataReader reader)
    {
        return new CompanionPairingRecord
        {
            PairingId = reader.GetString(reader.GetOrdinal("pairing_id")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            DesktopDeviceId = reader.GetString(reader.GetOrdinal("desktop_device_id")),
            DesktopDeviceLabel = reader.GetString(reader.GetOrdinal("desktop_device_label")),
            DesktopPlatform = reader.GetString(reader.GetOrdinal("desktop_platform")),
            CompanionDeviceId = reader.GetString(reader.GetOrdinal("companion_device_id")),
            CompanionDeviceLabel = reader.GetString(reader.GetOrdinal("companion_device_label")),
            CompanionPlatform = reader.GetString(reader.GetOrdinal("companion_platform")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            PairedAtUtc = reader.IsDBNull(reader.GetOrdinal("paired_at_utc")) ? null : reader.GetDateTime(reader.GetOrdinal("paired_at_utc")),
            RevokedAtUtc = reader.IsDBNull(reader.GetOrdinal("revoked_at_utc")) ? null : reader.GetDateTime(reader.GetOrdinal("revoked_at_utc"))
        };
    }
}
