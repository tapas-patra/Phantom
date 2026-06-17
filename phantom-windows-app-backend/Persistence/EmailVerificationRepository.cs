using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class EmailVerificationRepository
{
    private readonly PostgresBackendStore _store;

    public EmailVerificationRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public void Save(EmailVerificationTokenRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO email_verification_tokens (
    token_hash, user_id, email, expires_at_utc, created_at_utc, consumed, consumed_at_utc, delivery_status, delivery_error
) VALUES (
    @tokenHash, @userId, @email, @expiresAtUtc, @createdAtUtc, @consumed, @consumedAtUtc, @deliveryStatus, @deliveryError
)
ON CONFLICT(token_hash) DO UPDATE SET
    user_id = EXCLUDED.user_id,
    email = EXCLUDED.email,
    expires_at_utc = EXCLUDED.expires_at_utc,
    consumed = EXCLUDED.consumed,
    consumed_at_utc = EXCLUDED.consumed_at_utc,
    delivery_status = EXCLUDED.delivery_status,
    delivery_error = EXCLUDED.delivery_error;";
        Bind(command, record);
        command.ExecuteNonQuery();
    }

    public EmailVerificationTokenRecord? FindByTokenHash(string tokenHash)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM email_verification_tokens WHERE token_hash = @tokenHash LIMIT 1;";
        command.Parameters.AddWithValue("tokenHash", tokenHash);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    private static void Bind(NpgsqlCommand command, EmailVerificationTokenRecord record)
    {
        command.Parameters.AddWithValue("tokenHash", record.TokenHash);
        command.Parameters.AddWithValue("userId", record.UserId);
        command.Parameters.AddWithValue("email", record.Email);
        command.Parameters.AddWithValue("expiresAtUtc", record.ExpiresAtUtc);
        command.Parameters.AddWithValue("createdAtUtc", record.CreatedAtUtc);
        command.Parameters.AddWithValue("consumed", record.Consumed);
        command.Parameters.AddWithValue("consumedAtUtc", (object?)record.ConsumedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("deliveryStatus", record.DeliveryStatus ?? string.Empty);
        command.Parameters.AddWithValue("deliveryError", string.IsNullOrWhiteSpace(record.DeliveryError) ? DBNull.Value : record.DeliveryError);
    }

    private static EmailVerificationTokenRecord Map(NpgsqlDataReader reader)
    {
        return new EmailVerificationTokenRecord
        {
            TokenHash = reader.GetString(reader.GetOrdinal("token_hash")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            Email = reader.GetString(reader.GetOrdinal("email")),
            ExpiresAtUtc = reader.GetDateTime(reader.GetOrdinal("expires_at_utc")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            Consumed = reader.GetBoolean(reader.GetOrdinal("consumed")),
            ConsumedAtUtc = reader.IsDBNull(reader.GetOrdinal("consumed_at_utc"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("consumed_at_utc")),
            DeliveryStatus = reader.GetString(reader.GetOrdinal("delivery_status")),
            DeliveryError = reader.IsDBNull(reader.GetOrdinal("delivery_error"))
                ? string.Empty
                : reader.GetString(reader.GetOrdinal("delivery_error"))
        };
    }
}
