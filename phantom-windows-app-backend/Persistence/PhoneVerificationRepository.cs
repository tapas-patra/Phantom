using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class PhoneVerificationRepository
{
    private readonly PostgresBackendStore _store;

    public PhoneVerificationRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public PhoneVerificationChallengeRecord? FindById(string challengeId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM phone_verification_challenges WHERE challenge_id = @challengeId LIMIT 1;";
        command.Parameters.AddWithValue("challengeId", challengeId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public PhoneVerificationChallengeRecord? FindByVerificationTokenHash(string verificationTokenHash)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM phone_verification_challenges
WHERE verification_token_hash = @verificationTokenHash
LIMIT 1;";
        command.Parameters.AddWithValue("verificationTokenHash", verificationTokenHash);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public int CountRecentByPhone(string phoneNumberE164, TimeSpan lookback)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT COUNT(*)
FROM phone_verification_challenges
WHERE phone_number_e164 = @phoneNumber
  AND created_at_utc >= @cutoff;";
        command.Parameters.AddWithValue("phoneNumber", phoneNumberE164);
        command.Parameters.AddWithValue("cutoff", DateTime.UtcNow.Subtract(lookback));
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    public int CountRecentByFingerprint(string fingerprint, TimeSpan lookback)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT COUNT(*)
FROM phone_verification_challenges
WHERE device_fingerprint_hash = @fingerprint
  AND created_at_utc >= @cutoff;";
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        command.Parameters.AddWithValue("cutoff", DateTime.UtcNow.Subtract(lookback));
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    public void Save(PhoneVerificationChallengeRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO phone_verification_challenges (
    challenge_id, phone_number_e164, phone_number_masked, device_fingerprint_hash, install_id, email_hint,
    provider_name, provider_session_id, created_at_utc, expires_at_utc, verified_at_utc, consumed_at_utc,
    cooldown_until_utc, send_attempt_count, verify_attempt_count, status, verification_token_hash, failure_reason
) VALUES (
    @challengeId, @phoneNumberE164, @phoneNumberMasked, @deviceFingerprintHash, @installId, @emailHint,
    @providerName, @providerSessionId, @createdAtUtc, @expiresAtUtc, @verifiedAtUtc, @consumedAtUtc,
    @cooldownUntilUtc, @sendAttemptCount, @verifyAttemptCount, @status, @verificationTokenHash, @failureReason
)
ON CONFLICT(challenge_id) DO UPDATE SET
    provider_session_id = EXCLUDED.provider_session_id,
    expires_at_utc = EXCLUDED.expires_at_utc,
    verified_at_utc = EXCLUDED.verified_at_utc,
    consumed_at_utc = EXCLUDED.consumed_at_utc,
    cooldown_until_utc = EXCLUDED.cooldown_until_utc,
    send_attempt_count = EXCLUDED.send_attempt_count,
    verify_attempt_count = EXCLUDED.verify_attempt_count,
    status = EXCLUDED.status,
    verification_token_hash = EXCLUDED.verification_token_hash,
    failure_reason = EXCLUDED.failure_reason;";
        Bind(command, record);
        command.ExecuteNonQuery();
    }

    private static void Bind(NpgsqlCommand command, PhoneVerificationChallengeRecord record)
    {
        command.Parameters.AddWithValue("challengeId", record.ChallengeId);
        command.Parameters.AddWithValue("phoneNumberE164", record.PhoneNumberE164);
        command.Parameters.AddWithValue("phoneNumberMasked", record.PhoneNumberMasked);
        command.Parameters.AddWithValue("deviceFingerprintHash", record.DeviceFingerprintHash);
        command.Parameters.AddWithValue("installId", record.InstallId);
        command.Parameters.AddWithValue("emailHint", record.EmailHint);
        command.Parameters.AddWithValue("providerName", record.ProviderName);
        command.Parameters.AddWithValue("providerSessionId", record.ProviderSessionId);
        command.Parameters.AddWithValue("createdAtUtc", record.CreatedAtUtc);
        command.Parameters.AddWithValue("expiresAtUtc", record.ExpiresAtUtc);
        command.Parameters.AddWithValue("verifiedAtUtc", (object?)record.VerifiedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("consumedAtUtc", (object?)record.ConsumedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("cooldownUntilUtc", (object?)record.CooldownUntilUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("sendAttemptCount", record.SendAttemptCount);
        command.Parameters.AddWithValue("verifyAttemptCount", record.VerifyAttemptCount);
        command.Parameters.AddWithValue("status", record.Status);
        command.Parameters.AddWithValue("verificationTokenHash", record.VerificationTokenHash);
        command.Parameters.AddWithValue("failureReason", record.FailureReason);
    }

    private static PhoneVerificationChallengeRecord Map(NpgsqlDataReader reader)
    {
        return new PhoneVerificationChallengeRecord
        {
            ChallengeId = reader.GetString(reader.GetOrdinal("challenge_id")),
            PhoneNumberE164 = reader.GetString(reader.GetOrdinal("phone_number_e164")),
            PhoneNumberMasked = reader.GetString(reader.GetOrdinal("phone_number_masked")),
            DeviceFingerprintHash = reader.GetString(reader.GetOrdinal("device_fingerprint_hash")),
            InstallId = reader.GetString(reader.GetOrdinal("install_id")),
            EmailHint = reader.GetString(reader.GetOrdinal("email_hint")),
            ProviderName = reader.GetString(reader.GetOrdinal("provider_name")),
            ProviderSessionId = reader.GetString(reader.GetOrdinal("provider_session_id")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            ExpiresAtUtc = reader.GetDateTime(reader.GetOrdinal("expires_at_utc")),
            VerifiedAtUtc = reader.IsDBNull(reader.GetOrdinal("verified_at_utc"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("verified_at_utc")),
            ConsumedAtUtc = reader.IsDBNull(reader.GetOrdinal("consumed_at_utc"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("consumed_at_utc")),
            CooldownUntilUtc = reader.IsDBNull(reader.GetOrdinal("cooldown_until_utc"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("cooldown_until_utc")),
            SendAttemptCount = reader.GetInt32(reader.GetOrdinal("send_attempt_count")),
            VerifyAttemptCount = reader.GetInt32(reader.GetOrdinal("verify_attempt_count")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            VerificationTokenHash = reader.GetString(reader.GetOrdinal("verification_token_hash")),
            FailureReason = reader.GetString(reader.GetOrdinal("failure_reason"))
        };
    }
}
