using Microsoft.Data.Sqlite;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class MagicLinkRepository
{
    private readonly SqliteBackendStore _store;

    public MagicLinkRepository(SqliteBackendStore store)
    {
        _store = store;
    }

    public void Save(MagicLinkRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO magic_links (
    token, email, install_id, device_fingerprint_hash, expires_at_utc, created_at_utc, consumed, consumed_at_utc
) VALUES (
    $token, $email, $installId, $deviceFingerprintHash, $expiresAt, $createdAt, $consumed, $consumedAt
)
ON CONFLICT(token) DO UPDATE SET
    email = excluded.email,
    install_id = excluded.install_id,
    device_fingerprint_hash = excluded.device_fingerprint_hash,
    expires_at_utc = excluded.expires_at_utc,
    consumed = excluded.consumed,
    consumed_at_utc = excluded.consumed_at_utc;";
        command.Parameters.AddWithValue("$token", record.Token);
        command.Parameters.AddWithValue("$email", record.Email);
        command.Parameters.AddWithValue("$installId", record.InstallId);
        command.Parameters.AddWithValue("$deviceFingerprintHash", record.DeviceFingerprintHash);
        command.Parameters.AddWithValue("$expiresAt", record.ExpiresAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$createdAt", record.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$consumed", record.Consumed ? 1 : 0);
        command.Parameters.AddWithValue("$consumedAt", record.ConsumedAtUtc?.ToString("O") ?? (object)DBNull.Value);
        command.ExecuteNonQuery();
    }

    public MagicLinkRecord? FindByToken(string token)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM magic_links WHERE token = $token LIMIT 1;";
        command.Parameters.AddWithValue("$token", token);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new MagicLinkRecord
        {
            Token = reader.GetString(reader.GetOrdinal("token")),
            Email = reader.GetString(reader.GetOrdinal("email")),
            InstallId = reader.GetString(reader.GetOrdinal("install_id")),
            DeviceFingerprintHash = reader.GetString(reader.GetOrdinal("device_fingerprint_hash")),
            ExpiresAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("expires_at_utc")), null, System.Globalization.DateTimeStyles.RoundtripKind),
            CreatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at_utc")), null, System.Globalization.DateTimeStyles.RoundtripKind),
            Consumed = reader.GetInt32(reader.GetOrdinal("consumed")) == 1,
            ConsumedAtUtc = reader.IsDBNull(reader.GetOrdinal("consumed_at_utc"))
                ? null
                : DateTime.Parse(reader.GetString(reader.GetOrdinal("consumed_at_utc")), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }
}
