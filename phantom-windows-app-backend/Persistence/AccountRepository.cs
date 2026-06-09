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
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM desktop_accounts WHERE user_id = @userId LIMIT 1;";
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

    public void Save(DesktopAccountRecord account)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO desktop_accounts (
    user_id, email, access_tier, password_hash, phone_verified, pro_available_credits, premium_available_credits,
    premium_negative_credits, lease_expires_at_utc, offline_mode_enabled, last_validated_at_utc,
    created_at_utc, updated_at_utc
) VALUES (
    @userId, @email, @accessTier, @passwordHash, @phoneVerified, @proCredits, @premiumCredits,
    @premiumNegative, @leaseExpiresAt, @offlineModeEnabled, @lastValidatedAt, @createdAt, @updatedAt
)
ON CONFLICT(user_id) DO UPDATE SET
    email = EXCLUDED.email,
    access_tier = EXCLUDED.access_tier,
    password_hash = EXCLUDED.password_hash,
    phone_verified = EXCLUDED.phone_verified,
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

    private static void Bind(NpgsqlCommand command, DesktopAccountRecord account)
    {
        command.Parameters.AddWithValue("userId", account.UserId);
        command.Parameters.AddWithValue("email", account.Email);
        command.Parameters.AddWithValue("accessTier", account.AccessTier);
        command.Parameters.AddWithValue("passwordHash", account.PasswordHash);
        command.Parameters.AddWithValue("phoneVerified", account.PhoneVerified);
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
            AccessTier = reader.GetString(reader.GetOrdinal("access_tier")),
            PasswordHash = reader.GetString(reader.GetOrdinal("password_hash")),
            PhoneVerified = reader.GetBoolean(reader.GetOrdinal("phone_verified")),
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
