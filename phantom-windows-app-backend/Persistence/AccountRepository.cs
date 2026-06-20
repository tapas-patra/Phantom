using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class AccountRepository
{
    private readonly PostgresBackendStore _store;

    public AccountRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public DesktopAccountRecord? FindByUserId(string userId)
    {
        using var connection = _store.OpenConnection();
        return FindByUserId(userId, connection, transaction: null, forUpdate: false);
    }

    public DesktopAccountRecord? FindByUserId(
        string userId,
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        bool forUpdate)
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"SELECT * FROM desktop_accounts WHERE user_id = @userId LIMIT 1{(forUpdate ? " FOR UPDATE" : string.Empty)};";
        command.Parameters.AddWithValue("userId", userId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public DesktopAccountRecord? FindByEmail(string email)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM desktop_accounts WHERE lower(email) = lower(@email) LIMIT 1;";
        command.Parameters.AddWithValue("email", email);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public List<DesktopAccountRecord> ListAll()
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM desktop_accounts ORDER BY email ASC;";
        using var reader = command.ExecuteReader();
        var items = new List<DesktopAccountRecord>();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    public DesktopAccountRecord? FindByPhoneNumber(string phoneNumberE164)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM desktop_accounts WHERE phone_number_e164 = @phoneNumber LIMIT 1;";
        command.Parameters.AddWithValue("phoneNumber", phoneNumberE164);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public DesktopAccountRecord? FindByRegistrationFingerprint(string fingerprint)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM desktop_accounts WHERE registration_device_fingerprint_hash = @fingerprint LIMIT 1;";
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Save(DesktopAccountRecord account)
    {
        using var connection = _store.OpenConnection();
        Save(account, connection, transaction: null);
    }

    public void Save(DesktopAccountRecord account, NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText = @"
INSERT INTO desktop_accounts (
    user_id, email, email_verified, email_verified_at_utc, access_tier, password_hash, phone_number_e164, phone_verified, phone_verified_at_utc, registration_device_fingerprint_hash, pro_available_credits, premium_available_credits,
    premium_negative_credits, lease_expires_at_utc, offline_mode_enabled, last_validated_at_utc,
    created_at_utc, updated_at_utc
) VALUES (
    @userId, @email, @emailVerified, @emailVerifiedAtUtc, @accessTier, @passwordHash, @phoneNumberE164, @phoneVerified, @phoneVerifiedAtUtc, @registrationDeviceFingerprintHash, @proCredits, @premiumCredits,
    @premiumNegative, @leaseExpiresAt, @offlineModeEnabled, @lastValidatedAt, @createdAt, @updatedAt
)
ON CONFLICT(user_id) DO UPDATE SET
    email = EXCLUDED.email,
    email_verified = EXCLUDED.email_verified,
    email_verified_at_utc = EXCLUDED.email_verified_at_utc,
    access_tier = EXCLUDED.access_tier,
    password_hash = EXCLUDED.password_hash,
    phone_number_e164 = EXCLUDED.phone_number_e164,
    phone_verified = EXCLUDED.phone_verified,
    phone_verified_at_utc = EXCLUDED.phone_verified_at_utc,
    registration_device_fingerprint_hash = EXCLUDED.registration_device_fingerprint_hash,
    pro_available_credits = EXCLUDED.pro_available_credits,
    premium_available_credits = EXCLUDED.premium_available_credits,
    premium_negative_credits = EXCLUDED.premium_negative_credits,
    lease_expires_at_utc = EXCLUDED.lease_expires_at_utc,
    offline_mode_enabled = EXCLUDED.offline_mode_enabled,
    last_validated_at_utc = EXCLUDED.last_validated_at_utc,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        Bind(command, account);
        command.ExecuteNonQuery();
    }

    private static NpgsqlCommand CreateCommand(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        return command;
    }

    private static void Bind(NpgsqlCommand command, DesktopAccountRecord account)
    {
        command.Parameters.AddWithValue("userId", account.UserId);
        command.Parameters.AddWithValue("email", account.Email);
        command.Parameters.AddWithValue("emailVerified", account.EmailVerified);
        command.Parameters.AddWithValue("emailVerifiedAtUtc", (object?)account.EmailVerifiedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("accessTier", account.AccessTier);
        command.Parameters.AddWithValue("passwordHash", account.PasswordHash);
        command.Parameters.AddWithValue("phoneNumberE164", account.PhoneNumberE164);
        command.Parameters.AddWithValue("phoneVerified", account.PhoneVerified);
        command.Parameters.AddWithValue("phoneVerifiedAtUtc", (object?)account.PhoneVerifiedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("registrationDeviceFingerprintHash", account.RegistrationDeviceFingerprintHash);
        command.Parameters.AddWithValue("proCredits", account.ProAvailableCredits);
        command.Parameters.AddWithValue("premiumCredits", account.PremiumAvailableCredits);
        command.Parameters.AddWithValue("premiumNegative", account.PremiumNegativeCredits);
        command.Parameters.AddWithValue("leaseExpiresAt", account.LeaseExpiresAtUtc);
        command.Parameters.AddWithValue("offlineModeEnabled", account.OfflineModeEnabled);
        command.Parameters.AddWithValue("lastValidatedAt", account.LastValidatedAtUtc);
        command.Parameters.AddWithValue("createdAt", account.CreatedAtUtc);
        command.Parameters.AddWithValue("updatedAt", account.UpdatedAtUtc);
    }

    private static DesktopAccountRecord Map(NpgsqlDataReader reader)
    {
        return new DesktopAccountRecord
        {
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            Email = reader.GetString(reader.GetOrdinal("email")),
            EmailVerified = reader.GetBoolean(reader.GetOrdinal("email_verified")),
            EmailVerifiedAtUtc = reader.IsDBNull(reader.GetOrdinal("email_verified_at_utc"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("email_verified_at_utc")),
            AccessTier = reader.GetString(reader.GetOrdinal("access_tier")),
            PasswordHash = reader.GetString(reader.GetOrdinal("password_hash")),
            PhoneNumberE164 = reader.GetString(reader.GetOrdinal("phone_number_e164")),
            PhoneVerified = reader.GetBoolean(reader.GetOrdinal("phone_verified")),
            PhoneVerifiedAtUtc = reader.IsDBNull(reader.GetOrdinal("phone_verified_at_utc"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("phone_verified_at_utc")),
            RegistrationDeviceFingerprintHash = reader.GetString(reader.GetOrdinal("registration_device_fingerprint_hash")),
            ProAvailableCredits = reader.GetDecimal(reader.GetOrdinal("pro_available_credits")),
            PremiumAvailableCredits = reader.GetDecimal(reader.GetOrdinal("premium_available_credits")),
            PremiumNegativeCredits = reader.GetDecimal(reader.GetOrdinal("premium_negative_credits")),
            LeaseExpiresAtUtc = reader.GetDateTime(reader.GetOrdinal("lease_expires_at_utc")),
            OfflineModeEnabled = reader.GetBoolean(reader.GetOrdinal("offline_mode_enabled")),
            LastValidatedAtUtc = reader.GetDateTime(reader.GetOrdinal("last_validated_at_utc")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
        };
    }
}
