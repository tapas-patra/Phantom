using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class AdminPasswordResetRepository
{
    private readonly PostgresBackendStore _store;

    public AdminPasswordResetRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public AdminPasswordResetTokenRecord? FindByTokenHash(string tokenHash)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM admin_password_reset_tokens WHERE token_hash = @tokenHash LIMIT 1;";
        command.Parameters.AddWithValue("tokenHash", tokenHash);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Save(AdminPasswordResetTokenRecord token)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO admin_password_reset_tokens (
    token_hash, admin_id, email, expires_at_utc, created_at_utc, consumed, consumed_at_utc, delivery_status, delivery_error
) VALUES (
    @tokenHash, @adminId, @email, @expiresAtUtc, @createdAtUtc, @consumed, @consumedAtUtc, @deliveryStatus, @deliveryError
)
ON CONFLICT(token_hash) DO UPDATE SET
    admin_id = EXCLUDED.admin_id,
    email = EXCLUDED.email,
    expires_at_utc = EXCLUDED.expires_at_utc,
    consumed = EXCLUDED.consumed,
    consumed_at_utc = EXCLUDED.consumed_at_utc,
    delivery_status = EXCLUDED.delivery_status,
    delivery_error = EXCLUDED.delivery_error;";
        Bind(command, token);
        command.ExecuteNonQuery();
    }

    private static void Bind(NpgsqlCommand command, AdminPasswordResetTokenRecord token)
    {
        command.Parameters.AddWithValue("tokenHash", token.TokenHash);
        command.Parameters.AddWithValue("adminId", token.AdminId);
        command.Parameters.AddWithValue("email", token.Email);
        command.Parameters.AddWithValue("expiresAtUtc", token.ExpiresAtUtc);
        command.Parameters.AddWithValue("createdAtUtc", token.CreatedAtUtc);
        command.Parameters.AddWithValue("consumed", token.Consumed);
        command.Parameters.AddWithValue("consumedAtUtc", (object?)token.ConsumedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("deliveryStatus", token.DeliveryStatus);
        command.Parameters.AddWithValue("deliveryError", token.DeliveryError);
    }

    private static AdminPasswordResetTokenRecord Map(NpgsqlDataReader reader)
    {
        var consumedAtOrdinal = reader.GetOrdinal("consumed_at_utc");
        return new AdminPasswordResetTokenRecord
        {
            TokenHash = reader.GetString(reader.GetOrdinal("token_hash")),
            AdminId = reader.GetString(reader.GetOrdinal("admin_id")),
            Email = reader.GetString(reader.GetOrdinal("email")),
            ExpiresAtUtc = reader.GetDateTime(reader.GetOrdinal("expires_at_utc")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            Consumed = reader.GetBoolean(reader.GetOrdinal("consumed")),
            ConsumedAtUtc = reader.IsDBNull(consumedAtOrdinal) ? null : reader.GetDateTime(consumedAtOrdinal),
            DeliveryStatus = reader.GetString(reader.GetOrdinal("delivery_status")),
            DeliveryError = reader.GetString(reader.GetOrdinal("delivery_error"))
        };
    }
}
