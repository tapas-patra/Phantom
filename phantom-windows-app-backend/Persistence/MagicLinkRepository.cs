using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class MagicLinkRepository
{
    private readonly PostgresBackendStore _store;

    public MagicLinkRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public void Save(MagicLinkRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO magic_links (
    token_hash, email, install_id, device_fingerprint_hash, expires_at_utc, created_at_utc,
    consumed, consumed_at_utc, delivery_status, delivery_error
) VALUES (
    @tokenHash, @email, @installId, @deviceFingerprintHash, @expiresAt, @createdAt,
    @consumed, @consumedAt, @deliveryStatus, @deliveryError
)
ON CONFLICT(token_hash) DO UPDATE SET
    email = EXCLUDED.email,
    install_id = EXCLUDED.install_id,
    device_fingerprint_hash = EXCLUDED.device_fingerprint_hash,
    expires_at_utc = EXCLUDED.expires_at_utc,
    consumed = EXCLUDED.consumed,
    consumed_at_utc = EXCLUDED.consumed_at_utc,
    delivery_status = EXCLUDED.delivery_status,
    delivery_error = EXCLUDED.delivery_error;";
        command.Parameters.AddWithValue("tokenHash", record.TokenHash);
        command.Parameters.AddWithValue("email", record.Email);
        command.Parameters.AddWithValue("installId", record.InstallId);
        command.Parameters.AddWithValue("deviceFingerprintHash", record.DeviceFingerprintHash);
        command.Parameters.AddWithValue("expiresAt", record.ExpiresAtUtc);
        command.Parameters.AddWithValue("createdAt", record.CreatedAtUtc);
        command.Parameters.AddWithValue("consumed", record.Consumed);
        command.Parameters.AddWithValue("consumedAt", record.ConsumedAtUtc ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("deliveryStatus", record.DeliveryStatus);
        command.Parameters.AddWithValue("deliveryError", string.IsNullOrWhiteSpace(record.DeliveryError) ? (object)DBNull.Value : record.DeliveryError);
        command.ExecuteNonQuery();
    }

    public MagicLinkRecord? FindByTokenHash(string tokenHash)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM magic_links WHERE token_hash = @tokenHash LIMIT 1;";
        command.Parameters.AddWithValue("tokenHash", tokenHash);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void DeleteExpired()
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM magic_links WHERE expires_at_utc < @cutoff;";
        command.Parameters.AddWithValue("cutoff", DateTime.UtcNow.AddDays(-1));
        command.ExecuteNonQuery();
    }

    private static MagicLinkRecord Map(NpgsqlDataReader reader)
    {
        var consumedAtOrdinal = reader.GetOrdinal("consumed_at_utc");
        var deliveryErrorOrdinal = reader.GetOrdinal("delivery_error");
        return new MagicLinkRecord
        {
            TokenHash = reader.GetString(reader.GetOrdinal("token_hash")),
            Email = reader.GetString(reader.GetOrdinal("email")),
            InstallId = reader.GetString(reader.GetOrdinal("install_id")),
            DeviceFingerprintHash = reader.GetString(reader.GetOrdinal("device_fingerprint_hash")),
            ExpiresAtUtc = reader.GetDateTime(reader.GetOrdinal("expires_at_utc")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            Consumed = reader.GetBoolean(reader.GetOrdinal("consumed")),
            ConsumedAtUtc = reader.IsDBNull(consumedAtOrdinal) ? null : reader.GetDateTime(consumedAtOrdinal),
            DeliveryStatus = reader.GetString(reader.GetOrdinal("delivery_status")),
            DeliveryError = reader.IsDBNull(deliveryErrorOrdinal) ? string.Empty : reader.GetString(deliveryErrorOrdinal)
        };
    }
}
