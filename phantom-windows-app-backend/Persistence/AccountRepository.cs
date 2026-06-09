using Microsoft.Data.Sqlite;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class AccountRepository
{
    private readonly SqliteBackendStore _store;

    public AccountRepository(SqliteBackendStore store)
    {
        _store = store;
    }

    public DesktopAccountRecord? FindByUserId(string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM desktop_accounts WHERE user_id = $userId LIMIT 1;";
        command.Parameters.AddWithValue("$userId", userId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public DesktopAccountRecord? FindByEmail(string email)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM desktop_accounts WHERE lower(email) = lower($email) LIMIT 1;";
        command.Parameters.AddWithValue("$email", email);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Save(DesktopAccountRecord account)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO desktop_accounts (
    user_id, email, password_hash, phone_verified, pro_available_credits, premium_available_credits,
    premium_negative_credits, lease_expires_at_utc, offline_mode_enabled, last_validated_at_utc,
    created_at_utc, updated_at_utc
) VALUES (
    $userId, $email, $passwordHash, $phoneVerified, $proCredits, $premiumCredits,
    $premiumNegative, $leaseExpiresAt, $offlineModeEnabled, $lastValidatedAt, $createdAt, $updatedAt
)
ON CONFLICT(user_id) DO UPDATE SET
    email = excluded.email,
    password_hash = excluded.password_hash,
    phone_verified = excluded.phone_verified,
    pro_available_credits = excluded.pro_available_credits,
    premium_available_credits = excluded.premium_available_credits,
    premium_negative_credits = excluded.premium_negative_credits,
    lease_expires_at_utc = excluded.lease_expires_at_utc,
    offline_mode_enabled = excluded.offline_mode_enabled,
    last_validated_at_utc = excluded.last_validated_at_utc,
    updated_at_utc = excluded.updated_at_utc;
";
        Bind(command, account);
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, DesktopAccountRecord account)
    {
        command.Parameters.AddWithValue("$userId", account.UserId);
        command.Parameters.AddWithValue("$email", account.Email);
        command.Parameters.AddWithValue("$passwordHash", account.PasswordHash);
        command.Parameters.AddWithValue("$phoneVerified", account.PhoneVerified ? 1 : 0);
        command.Parameters.AddWithValue("$proCredits", account.ProAvailableCredits.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$premiumCredits", account.PremiumAvailableCredits.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$premiumNegative", account.PremiumNegativeCredits.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$leaseExpiresAt", account.LeaseExpiresAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$offlineModeEnabled", account.OfflineModeEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$lastValidatedAt", account.LastValidatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$createdAt", account.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", account.UpdatedAtUtc.ToString("O"));
    }

    private static DesktopAccountRecord Map(SqliteDataReader reader)
    {
        return new DesktopAccountRecord
        {
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            Email = reader.GetString(reader.GetOrdinal("email")),
            PasswordHash = reader.GetString(reader.GetOrdinal("password_hash")),
            PhoneVerified = reader.GetInt32(reader.GetOrdinal("phone_verified")) == 1,
            ProAvailableCredits = decimal.Parse(reader.GetString(reader.GetOrdinal("pro_available_credits")), System.Globalization.CultureInfo.InvariantCulture),
            PremiumAvailableCredits = decimal.Parse(reader.GetString(reader.GetOrdinal("premium_available_credits")), System.Globalization.CultureInfo.InvariantCulture),
            PremiumNegativeCredits = decimal.Parse(reader.GetString(reader.GetOrdinal("premium_negative_credits")), System.Globalization.CultureInfo.InvariantCulture),
            LeaseExpiresAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("lease_expires_at_utc")), null, System.Globalization.DateTimeStyles.RoundtripKind),
            OfflineModeEnabled = reader.GetInt32(reader.GetOrdinal("offline_mode_enabled")) == 1,
            LastValidatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("last_validated_at_utc")), null, System.Globalization.DateTimeStyles.RoundtripKind),
            CreatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at_utc")), null, System.Globalization.DateTimeStyles.RoundtripKind),
            UpdatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("updated_at_utc")), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }
}
