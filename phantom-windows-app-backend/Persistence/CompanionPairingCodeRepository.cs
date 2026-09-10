using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class CompanionPairingCodeRepository
{
    private readonly PostgresBackendStore _store;

    public CompanionPairingCodeRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public CompanionPairingCodeRecord? FindByHash(string codeHash)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM companion_pairing_codes WHERE code_hash = @codeHash LIMIT 1;";
        command.Parameters.AddWithValue("codeHash", codeHash);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Save(CompanionPairingCodeRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO companion_pairing_codes (
    code_hash, user_id, desktop_device_id, desktop_device_label, desktop_platform,
    app_version, expires_at_utc, consumed_at_utc
) VALUES (
    @codeHash, @userId, @desktopDeviceId, @desktopDeviceLabel, @desktopPlatform,
    @appVersion, @expiresAtUtc, @consumedAtUtc
)
ON CONFLICT(code_hash) DO UPDATE SET
    consumed_at_utc = EXCLUDED.consumed_at_utc,
    expires_at_utc = EXCLUDED.expires_at_utc;";
        command.Parameters.AddWithValue("codeHash", record.CodeHash);
        command.Parameters.AddWithValue("userId", record.UserId);
        command.Parameters.AddWithValue("desktopDeviceId", record.DesktopDeviceId);
        command.Parameters.AddWithValue("desktopDeviceLabel", record.DesktopDeviceLabel);
        command.Parameters.AddWithValue("desktopPlatform", record.DesktopPlatform);
        command.Parameters.AddWithValue("appVersion", record.AppVersion);
        command.Parameters.AddWithValue("expiresAtUtc", record.ExpiresAtUtc);
        command.Parameters.AddWithValue("consumedAtUtc", (object?)record.ConsumedAtUtc ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void MarkConsumed(string codeHash, DateTime consumedAtUtc)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE companion_pairing_codes SET consumed_at_utc = @consumedAtUtc WHERE code_hash = @codeHash;";
        command.Parameters.AddWithValue("codeHash", codeHash);
        command.Parameters.AddWithValue("consumedAtUtc", consumedAtUtc);
        command.ExecuteNonQuery();
    }

    public void DeleteUnusedForDevice(string desktopDeviceId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM companion_pairing_codes WHERE desktop_device_id = @desktopDeviceId AND consumed_at_utc IS NULL;";
        command.Parameters.AddWithValue("desktopDeviceId", desktopDeviceId);
        command.ExecuteNonQuery();
    }

    private static CompanionPairingCodeRecord Map(NpgsqlDataReader reader)
    {
        return new CompanionPairingCodeRecord
        {
            CodeHash = reader.GetString(reader.GetOrdinal("code_hash")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            DesktopDeviceId = reader.GetString(reader.GetOrdinal("desktop_device_id")),
            DesktopDeviceLabel = reader.GetString(reader.GetOrdinal("desktop_device_label")),
            DesktopPlatform = reader.GetString(reader.GetOrdinal("desktop_platform")),
            AppVersion = reader.GetString(reader.GetOrdinal("app_version")),
            ExpiresAtUtc = reader.GetDateTime(reader.GetOrdinal("expires_at_utc")),
            ConsumedAtUtc = reader.IsDBNull(reader.GetOrdinal("consumed_at_utc")) ? null : reader.GetDateTime(reader.GetOrdinal("consumed_at_utc"))
        };
    }
}
