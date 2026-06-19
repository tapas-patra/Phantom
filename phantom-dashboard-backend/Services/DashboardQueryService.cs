using Npgsql;
using Phantom.Dashboard.Backend.Persistence;

namespace Phantom.Dashboard.Backend.Services;

public sealed class DashboardQueryService
{
    private readonly PostgresDashboardStore _store;

    public DashboardQueryService(PostgresDashboardStore store)
    {
        _store = store;
    }

    public object? GetAccountSummary(string? userId, string? email)
    {
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("userId or email is required.");
        }

        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    user_id,
    email,
    access_tier,
    phone_verified,
    pro_available_credits,
    premium_available_credits,
    premium_negative_credits,
    lease_expires_at_utc,
    offline_mode_enabled,
    last_validated_at_utc
FROM desktop_accounts
WHERE
    (@userId <> '' AND user_id = @userId)
    OR (@email <> '' AND lower(email) = lower(@email))
ORDER BY updated_at_utc DESC
LIMIT 1;";
        command.Parameters.AddWithValue("userId", userId ?? string.Empty);
        command.Parameters.AddWithValue("email", email ?? string.Empty);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var resolvedUserId = reader.GetString(reader.GetOrdinal("user_id"));
        var resolvedEmail = reader.GetString(reader.GetOrdinal("email"));
        var activeDeviceCount = CountDistinctDevices(resolvedUserId);
        var lastActivityAtUtc = GetLastActivity(resolvedUserId);
        var proCredits = reader.GetDecimal(reader.GetOrdinal("pro_available_credits"));
        var premiumCredits = reader.GetDecimal(reader.GetOrdinal("premium_available_credits"));
        var accessTier = reader.GetString(reader.GetOrdinal("access_tier"));
        var effectiveTier = ResolveEffectiveTier(accessTier, proCredits, premiumCredits);

        return new
        {
            userId = resolvedUserId,
            email = resolvedEmail,
            effectiveAccessTier = effectiveTier,
            planLabel = effectiveTier.Equals("premium", StringComparison.OrdinalIgnoreCase)
                ? "Premium"
                : effectiveTier.Equals("pro_byo", StringComparison.OrdinalIgnoreCase)
                    ? "Pro BYO"
                    : "Free",
            phoneVerified = reader.GetBoolean(reader.GetOrdinal("phone_verified")),
            proAvailableCredits = proCredits,
            premiumAvailableCredits = premiumCredits,
            premiumNegativeCredits = reader.GetDecimal(reader.GetOrdinal("premium_negative_credits")),
            leaseExpiresAtUtc = reader.GetDateTime(reader.GetOrdinal("lease_expires_at_utc")),
            offlineModeEnabled = reader.GetBoolean(reader.GetOrdinal("offline_mode_enabled")),
            lastValidatedAtUtc = reader.GetDateTime(reader.GetOrdinal("last_validated_at_utc")),
            activeDeviceCount,
            lastActivityAtUtc
        };
    }

    public IReadOnlyList<object> GetWalletHistory(string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT ledger_entry_id, session_id, charged_credits, charged_blocks, added_premium_debt, created_at_utc
FROM usage_ledger
WHERE user_id = @userId
  AND session_id NOT LIKE 'payment:%'
ORDER BY created_at_utc DESC
LIMIT 50;";
        command.Parameters.AddWithValue("userId", userId);
        using var reader = command.ExecuteReader();
        var items = new List<object>();
        while (reader.Read())
        {
            items.Add(new
            {
                ledgerEntryId = reader.GetString(reader.GetOrdinal("ledger_entry_id")),
                sessionId = reader.GetString(reader.GetOrdinal("session_id")),
                chargedCredits = reader.GetDecimal(reader.GetOrdinal("charged_credits")),
                chargedBlocks = reader.GetInt32(reader.GetOrdinal("charged_blocks")),
                addedPremiumDebt = reader.GetDecimal(reader.GetOrdinal("added_premium_debt")),
                createdAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc"))
            });
        }

        return items;
    }

    public IReadOnlyList<object> GetWalletPurchases(string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT checkout_id, target, pack_code, display_label, amount_minor, credits, premium_debt_credits_covered, status, client_confirmed, credited_at_utc, created_at_utc
FROM payment_orders
WHERE user_id = @userId
ORDER BY created_at_utc DESC
LIMIT 50;";
        command.Parameters.AddWithValue("userId", userId);
        using var reader = command.ExecuteReader();
        var items = new List<object>();
        while (reader.Read())
        {
            items.Add(new
            {
                checkoutId = reader.GetString(reader.GetOrdinal("checkout_id")),
                target = reader.GetString(reader.GetOrdinal("target")),
                packCode = reader.GetString(reader.GetOrdinal("pack_code")),
                displayLabel = reader.GetString(reader.GetOrdinal("display_label")),
                amountMinor = reader.GetInt32(reader.GetOrdinal("amount_minor")),
                amountInr = reader.GetInt32(reader.GetOrdinal("amount_minor")) / 100m,
                credits = reader.GetDecimal(reader.GetOrdinal("credits")),
                premiumDebtCreditsCovered = reader.GetDecimal(reader.GetOrdinal("premium_debt_credits_covered")),
                status = reader.GetString(reader.GetOrdinal("status")),
                clientConfirmed = reader.GetBoolean(reader.GetOrdinal("client_confirmed")),
                creditedAtUtc = reader.IsDBNull(reader.GetOrdinal("credited_at_utc"))
                    ? (DateTime?)null
                    : reader.GetDateTime(reader.GetOrdinal("credited_at_utc")),
                createdAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc"))
            });
        }

        return items;
    }

    public IReadOnlyList<object> GetDevices(string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT DISTINCT ON (device_install_id, device_fingerprint_hash)
    device_install_id,
    device_fingerprint_hash,
    authenticated_at_utc,
    auth_method,
    is_authenticated,
    revoked_at_utc
FROM auth_sessions
WHERE user_id = @userId
ORDER BY device_install_id, device_fingerprint_hash, authenticated_at_utc DESC;";
        command.Parameters.AddWithValue("userId", userId);
        using var reader = command.ExecuteReader();
        var items = new List<object>();
        while (reader.Read())
        {
            items.Add(new
            {
                deviceInstallId = reader.GetString(reader.GetOrdinal("device_install_id")),
                deviceFingerprintHash = reader.GetString(reader.GetOrdinal("device_fingerprint_hash")),
                lastAuthenticatedAtUtc = reader.GetDateTime(reader.GetOrdinal("authenticated_at_utc")),
                authMethod = reader.GetString(reader.GetOrdinal("auth_method")),
                isActive = reader.GetBoolean(reader.GetOrdinal("is_authenticated"))
                    && reader.IsDBNull(reader.GetOrdinal("revoked_at_utc"))
            });
        }

        return items;
    }

    public object GetDownloadEntitlement(string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT phone_verified, pro_available_credits, premium_available_credits
FROM desktop_accounts
WHERE user_id = @userId
LIMIT 1;";
        command.Parameters.AddWithValue("userId", userId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("Account not found.");
        }

        var hasCredits = reader.GetDecimal(reader.GetOrdinal("pro_available_credits")) > 0m
            || reader.GetDecimal(reader.GetOrdinal("premium_available_credits")) > 0m;

        return new
        {
            canDownload = reader.GetBoolean(reader.GetOrdinal("phone_verified")),
            installerLabel = "Phantom Desktop for Windows",
            installerVersion = "0.9.0-preview",
            installerUrl = "/download",
            releaseChannel = hasCredits ? "Hosted Preview" : "Verification Pending"
        };
    }

    public object GetSupportPreview(string userId)
    {
        using var connection = _store.OpenConnection();
        var lockSessionId = "";
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
SELECT session_id
FROM interview_locks
WHERE user_id = @userId
ORDER BY expires_at_utc DESC
LIMIT 1;";
            command.Parameters.AddWithValue("userId", userId);
            lockSessionId = command.ExecuteScalar() as string ?? string.Empty;
        }

        decimal lastCharge = 0m;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
SELECT charged_credits
FROM usage_ledger
WHERE user_id = @userId
  AND session_id NOT LIKE 'payment:%'
ORDER BY created_at_utc DESC
LIMIT 1;";
            command.Parameters.AddWithValue("userId", userId);
            var result = command.ExecuteScalar();
            if (result != null && result != DBNull.Value)
            {
                lastCharge = Convert.ToDecimal(result);
            }
        }

        double leaseHoursRemaining = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
SELECT lease_expires_at_utc
FROM desktop_accounts
WHERE user_id = @userId
LIMIT 1;";
            command.Parameters.AddWithValue("userId", userId);
            var result = command.ExecuteScalar();
            if (result is DateTime leaseExpiresAtUtc)
            {
                leaseHoursRemaining = Math.Max(0, (leaseExpiresAtUtc - DateTime.UtcNow).TotalHours);
            }
        }

        return new
        {
            openLockSessionId = lockSessionId,
            lastUsageChargeCredits = lastCharge,
            offlineLeaseHoursRemaining = Math.Round(leaseHoursRemaining, 1),
            supportMessage = "Support dashboards can inspect wallet state, stale locks, and recent usage without mutating runtime authority."
        };
    }

    public object GetAdminOverview()
    {
        using var connection = _store.OpenConnection();
        return new
        {
            accountCount = ExecuteCount(connection, "SELECT COUNT(*) FROM desktop_accounts;"),
            activeSessionCount = ExecuteCount(connection, "SELECT COUNT(*) FROM auth_sessions WHERE is_authenticated = TRUE AND revoked_at_utc IS NULL;"),
            activeLockCount = ExecuteCount(connection, "SELECT COUNT(*) FROM interview_locks WHERE expires_at_utc > NOW();"),
            ledgerEntryCount = ExecuteCount(connection, "SELECT COUNT(*) FROM usage_ledger;"),
            managedCredentialCount = ExecuteCount(connection, "SELECT COUNT(*) FROM managed_provider_credentials;")
        };
    }

    private int CountDistinctDevices(string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT COUNT(*) FROM (
  SELECT DISTINCT device_install_id, device_fingerprint_hash
  FROM auth_sessions
  WHERE user_id = @userId
) AS device_rows;";
        command.Parameters.AddWithValue("userId", userId);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    private DateTime? GetLastActivity(string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT MAX(created_at_utc)
FROM usage_ledger
WHERE user_id = @userId
  AND session_id NOT LIKE 'payment:%';";
        command.Parameters.AddWithValue("userId", userId);
        var result = command.ExecuteScalar();
        return result == DBNull.Value || result == null ? null : Convert.ToDateTime(result);
    }

    private static int ExecuteCount(NpgsqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    private static string ResolveEffectiveTier(string accessTier, decimal proCredits, decimal premiumCredits)
    {
        if (accessTier.Equals("free", StringComparison.OrdinalIgnoreCase)
            && proCredits <= 0m
            && premiumCredits <= 0.5m)
        {
            return "free";
        }

        if (premiumCredits > 0m)
        {
            return "premium";
        }

        if (proCredits > 0m)
        {
            return "pro_byo";
        }

        return "free";
    }
}
