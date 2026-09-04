using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class AdminLoginChallengeRepository
{
    private readonly PostgresBackendStore _store;

    public AdminLoginChallengeRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public AdminLoginChallengeRecord? Find(string challengeId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM admin_login_challenges WHERE challenge_id = @challengeId LIMIT 1;";
        command.Parameters.AddWithValue("challengeId", challengeId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Save(AdminLoginChallengeRecord challenge)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO admin_login_challenges (
    challenge_id, admin_id, email, code_hash, expires_at_utc, created_at_utc,
    failed_attempts, consumed, consumed_at_utc, delivery_status, delivery_error
) VALUES (
    @challengeId, @adminId, @email, @codeHash, @expiresAtUtc, @createdAtUtc,
    @failedAttempts, @consumed, @consumedAtUtc, @deliveryStatus, @deliveryError
)
ON CONFLICT(challenge_id) DO UPDATE SET
    failed_attempts = EXCLUDED.failed_attempts,
    consumed = EXCLUDED.consumed,
    consumed_at_utc = EXCLUDED.consumed_at_utc,
    delivery_status = EXCLUDED.delivery_status,
    delivery_error = EXCLUDED.delivery_error;";
        command.Parameters.AddWithValue("challengeId", challenge.ChallengeId);
        command.Parameters.AddWithValue("adminId", challenge.AdminId);
        command.Parameters.AddWithValue("email", challenge.Email);
        command.Parameters.AddWithValue("codeHash", challenge.CodeHash);
        command.Parameters.AddWithValue("expiresAtUtc", challenge.ExpiresAtUtc);
        command.Parameters.AddWithValue("createdAtUtc", challenge.CreatedAtUtc);
        command.Parameters.AddWithValue("failedAttempts", challenge.FailedAttempts);
        command.Parameters.AddWithValue("consumed", challenge.Consumed);
        command.Parameters.AddWithValue("consumedAtUtc", (object?)challenge.ConsumedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("deliveryStatus", challenge.DeliveryStatus);
        command.Parameters.AddWithValue("deliveryError", challenge.DeliveryError);
        command.ExecuteNonQuery();
    }

    public void ConsumeActiveForAdmin(string adminId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE admin_login_challenges
SET consumed = TRUE, consumed_at_utc = NOW()
WHERE admin_id = @adminId AND consumed = FALSE;";
        command.Parameters.AddWithValue("adminId", adminId);
        command.ExecuteNonQuery();
    }

    public bool TryConsumeValid(string challengeId, string codeHash, DateTime consumedAtUtc)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE admin_login_challenges
SET consumed = TRUE, consumed_at_utc = @consumedAtUtc
WHERE challenge_id = @challengeId
  AND code_hash = @codeHash
  AND consumed = FALSE
  AND expires_at_utc > @consumedAtUtc;";
        command.Parameters.AddWithValue("challengeId", challengeId);
        command.Parameters.AddWithValue("codeHash", codeHash);
        command.Parameters.AddWithValue("consumedAtUtc", consumedAtUtc);
        return command.ExecuteNonQuery() == 1;
    }

    public AdminLoginChallengeRecord? RegisterFailedAttempt(string challengeId, int maxAttempts, DateTime attemptedAtUtc)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE admin_login_challenges
SET failed_attempts = failed_attempts + 1,
    consumed = failed_attempts + 1 >= @maxAttempts,
    consumed_at_utc = CASE
        WHEN failed_attempts + 1 >= @maxAttempts THEN @attemptedAtUtc
        ELSE consumed_at_utc
    END
WHERE challenge_id = @challengeId
  AND consumed = FALSE
  AND expires_at_utc > @attemptedAtUtc
RETURNING *;";
        command.Parameters.AddWithValue("challengeId", challengeId);
        command.Parameters.AddWithValue("maxAttempts", maxAttempts);
        command.Parameters.AddWithValue("attemptedAtUtc", attemptedAtUtc);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void DeleteExpired()
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM admin_login_challenges WHERE expires_at_utc < @cutoff;";
        command.Parameters.AddWithValue("cutoff", DateTime.UtcNow.AddDays(-1));
        command.ExecuteNonQuery();
    }

    private static AdminLoginChallengeRecord Map(NpgsqlDataReader reader)
    {
        var consumedAtOrdinal = reader.GetOrdinal("consumed_at_utc");
        return new AdminLoginChallengeRecord
        {
            ChallengeId = reader.GetString(reader.GetOrdinal("challenge_id")),
            AdminId = reader.GetString(reader.GetOrdinal("admin_id")),
            Email = reader.GetString(reader.GetOrdinal("email")),
            CodeHash = reader.GetString(reader.GetOrdinal("code_hash")),
            ExpiresAtUtc = reader.GetDateTime(reader.GetOrdinal("expires_at_utc")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            FailedAttempts = reader.GetInt32(reader.GetOrdinal("failed_attempts")),
            Consumed = reader.GetBoolean(reader.GetOrdinal("consumed")),
            ConsumedAtUtc = reader.IsDBNull(consumedAtOrdinal) ? null : reader.GetDateTime(consumedAtOrdinal),
            DeliveryStatus = reader.GetString(reader.GetOrdinal("delivery_status")),
            DeliveryError = reader.GetString(reader.GetOrdinal("delivery_error"))
        };
    }
}
