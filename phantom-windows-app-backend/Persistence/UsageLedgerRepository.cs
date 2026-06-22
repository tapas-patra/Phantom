using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class UsageLedgerRepository
{
    private readonly PostgresBackendStore _store;

    public UsageLedgerRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public UsageLedgerRecord? FindBySessionId(string sessionId)
    {
        using var connection = _store.OpenConnection();
        return FindBySessionId(sessionId, connection, transaction: null);
    }

    public UsageLedgerRecord? FindBySessionId(string sessionId, NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText = "SELECT * FROM usage_ledger WHERE session_id = @sessionId LIMIT 1;";
        command.Parameters.AddWithValue("sessionId", sessionId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Save(UsageLedgerRecord record)
    {
        using var connection = _store.OpenConnection();
        Save(record, connection, transaction: null);
    }

    public void Save(UsageLedgerRecord record, NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText = @"
INSERT INTO usage_ledger (
    ledger_entry_id, user_id, session_id, started_at_utc, ended_at_utc,
    charged_credits, charged_blocks, added_premium_debt, created_at_utc
) VALUES (
    @ledgerEntryId, @userId, @sessionId, @startedAt, @endedAt,
    @chargedCredits, @chargedBlocks, @addedDebt, @createdAt
);";
        command.Parameters.AddWithValue("ledgerEntryId", record.LedgerEntryId);
        command.Parameters.AddWithValue("userId", record.UserId);
        command.Parameters.AddWithValue("sessionId", record.SessionId);
        command.Parameters.AddWithValue("startedAt", record.StartedAtUtc);
        command.Parameters.AddWithValue("endedAt", record.EndedAtUtc);
        command.Parameters.AddWithValue("chargedCredits", record.ChargedCredits);
        command.Parameters.AddWithValue("chargedBlocks", record.ChargedBlocks);
        command.Parameters.AddWithValue("addedDebt", record.AddedPremiumDebt);
        command.Parameters.AddWithValue("createdAt", record.CreatedAtUtc);
        command.ExecuteNonQuery();
    }

    public void SaveReconciliation(DesktopAccountRecord account, UsageLedgerRecord ledger)
    {
        using var connection = _store.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = FindBySessionId(ledger.SessionId, connection, transaction);
        if (existing != null)
        {
            transaction.Commit();
            return;
        }

        using (var accountCommand = CreateCommand(connection, transaction))
        {
            accountCommand.CommandText = @"
UPDATE desktop_accounts
SET email = @email,
    email_verified = @emailVerified,
    email_verified_at_utc = @emailVerifiedAtUtc,
    access_tier = @accessTier,
    password_hash = @passwordHash,
    phone_number_e164 = @phoneNumberE164,
    phone_verified = @phoneVerified,
    phone_verified_at_utc = @phoneVerifiedAtUtc,
    registration_device_fingerprint_hash = @registrationDeviceFingerprintHash,
    pro_available_credits = @proCredits,
    premium_available_credits = @premiumCredits,
    premium_negative_credits = @premiumNegative,
    lease_expires_at_utc = @leaseExpiresAt,
    offline_mode_enabled = @offlineModeEnabled,
    last_validated_at_utc = @lastValidatedAt,
    updated_at_utc = @updatedAt
WHERE user_id = @userId;";
            accountCommand.Parameters.AddWithValue("userId", account.UserId);
            accountCommand.Parameters.AddWithValue("email", account.Email);
            accountCommand.Parameters.AddWithValue("emailVerified", account.EmailVerified);
            accountCommand.Parameters.AddWithValue("emailVerifiedAtUtc", (object?)account.EmailVerifiedAtUtc ?? DBNull.Value);
            accountCommand.Parameters.AddWithValue("accessTier", account.AccessTier);
            accountCommand.Parameters.AddWithValue("passwordHash", account.PasswordHash);
            accountCommand.Parameters.AddWithValue("phoneNumberE164", account.PhoneNumberE164);
            accountCommand.Parameters.AddWithValue("phoneVerified", account.PhoneVerified);
            accountCommand.Parameters.AddWithValue("phoneVerifiedAtUtc", (object?)account.PhoneVerifiedAtUtc ?? DBNull.Value);
            accountCommand.Parameters.AddWithValue("registrationDeviceFingerprintHash", account.RegistrationDeviceFingerprintHash);
            accountCommand.Parameters.AddWithValue("proCredits", account.ProAvailableCredits);
            accountCommand.Parameters.AddWithValue("premiumCredits", account.PremiumAvailableCredits);
            accountCommand.Parameters.AddWithValue("premiumNegative", account.PremiumNegativeCredits);
            accountCommand.Parameters.AddWithValue("leaseExpiresAt", account.LeaseExpiresAtUtc);
            accountCommand.Parameters.AddWithValue("offlineModeEnabled", account.OfflineModeEnabled);
            accountCommand.Parameters.AddWithValue("lastValidatedAt", account.LastValidatedAtUtc);
            accountCommand.Parameters.AddWithValue("updatedAt", account.UpdatedAtUtc);
            var updated = accountCommand.ExecuteNonQuery();
            if (updated != 1)
            {
                throw new InvalidOperationException("Usage reconciliation account update failed.");
            }
        }

        Save(ledger, connection, transaction);
        transaction.Commit();
    }

    private static NpgsqlCommand CreateCommand(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        return command;
    }

    public List<UsageLedgerRecord> ListRecentForUser(string userId, int maxCount)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM usage_ledger
WHERE user_id = @userId
ORDER BY created_at_utc DESC
LIMIT @maxCount;";
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("maxCount", Math.Max(1, maxCount));
        using var reader = command.ExecuteReader();
        var items = new List<UsageLedgerRecord>();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    public List<UsageLedgerRecord> ListPageForUser(string userId, int offset, int pageSize)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM usage_ledger
WHERE user_id = @userId
ORDER BY created_at_utc DESC
OFFSET @offset
LIMIT @pageSize;";
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("offset", Math.Max(0, offset));
        command.Parameters.AddWithValue("pageSize", Math.Max(1, pageSize));
        using var reader = command.ExecuteReader();
        var items = new List<UsageLedgerRecord>();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    public int CountForUser(string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM usage_ledger WHERE user_id = @userId;";
        command.Parameters.AddWithValue("userId", userId);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    private static UsageLedgerRecord Map(NpgsqlDataReader reader)
    {
        return new UsageLedgerRecord
        {
            LedgerEntryId = reader.GetString(reader.GetOrdinal("ledger_entry_id")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            SessionId = reader.GetString(reader.GetOrdinal("session_id")),
            StartedAtUtc = reader.GetDateTime(reader.GetOrdinal("started_at_utc")),
            EndedAtUtc = reader.GetDateTime(reader.GetOrdinal("ended_at_utc")),
            ChargedCredits = reader.GetDecimal(reader.GetOrdinal("charged_credits")),
            ChargedBlocks = reader.GetInt32(reader.GetOrdinal("charged_blocks")),
            AddedPremiumDebt = reader.GetDecimal(reader.GetOrdinal("added_premium_debt")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc"))
        };
    }
}
