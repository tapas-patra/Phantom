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
    effective_access_tier,
    plan_label,
    phone_verified,
    pro_available_credits,
    premium_available_credits,
    premium_negative_credits,
    lease_expires_at_utc,
    offline_mode_enabled,
    can_use_desktop_power_features,
    last_validated_at_utc,
    active_device_count,
    last_activity_at_utc
FROM dashboard_account_summaries
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

        return new
        {
            userId = reader.GetString(reader.GetOrdinal("user_id")),
            email = reader.GetString(reader.GetOrdinal("email")),
            effectiveAccessTier = reader.GetString(reader.GetOrdinal("effective_access_tier")),
            planLabel = reader.GetString(reader.GetOrdinal("plan_label")),
            phoneVerified = reader.GetBoolean(reader.GetOrdinal("phone_verified")),
            proAvailableCredits = reader.GetDecimal(reader.GetOrdinal("pro_available_credits")),
            premiumAvailableCredits = reader.GetDecimal(reader.GetOrdinal("premium_available_credits")),
            premiumNegativeCredits = reader.GetDecimal(reader.GetOrdinal("premium_negative_credits")),
            leaseExpiresAtUtc = reader.GetDateTime(reader.GetOrdinal("lease_expires_at_utc")),
            offlineModeEnabled = reader.GetBoolean(reader.GetOrdinal("offline_mode_enabled")),
            canUseDesktopPowerFeatures = reader.GetBoolean(reader.GetOrdinal("can_use_desktop_power_features")),
            lastValidatedAtUtc = reader.GetDateTime(reader.GetOrdinal("last_validated_at_utc")),
            activeDeviceCount = reader.GetInt32(reader.GetOrdinal("active_device_count")),
            lastActivityAtUtc = reader.IsDBNull(reader.GetOrdinal("last_activity_at_utc"))
                ? (DateTime?)null
                : reader.GetDateTime(reader.GetOrdinal("last_activity_at_utc"))
        };
    }

    public object GetWalletHistory(string userId, int page, int pageSize)
    {
        var normalizedPage = NormalizePage(page);
        var normalizedPageSize = NormalizePageSize(pageSize);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var items = new List<object>();
        using var connection = _store.OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
SELECT ledger_entry_id, session_id, charged_credits, charged_blocks, charged_pro_credits, charged_premium_credits, added_premium_debt, created_at_utc
FROM dashboard_wallet_history
WHERE user_id = @userId
ORDER BY created_at_utc DESC
OFFSET @offset
LIMIT @pageSize;";
            command.Parameters.AddWithValue("userId", userId);
            command.Parameters.AddWithValue("offset", offset);
            command.Parameters.AddWithValue("pageSize", normalizedPageSize);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new
                {
                    ledgerEntryId = reader.GetString(reader.GetOrdinal("ledger_entry_id")),
                    sessionId = reader.GetString(reader.GetOrdinal("session_id")),
                    chargedCredits = reader.GetDecimal(reader.GetOrdinal("charged_credits")),
                    chargedBlocks = reader.GetInt32(reader.GetOrdinal("charged_blocks")),
                    chargedProCredits = reader.GetDecimal(reader.GetOrdinal("charged_pro_credits")),
                    chargedPremiumCredits = reader.GetDecimal(reader.GetOrdinal("charged_premium_credits")),
                    addedPremiumDebt = reader.GetDecimal(reader.GetOrdinal("added_premium_debt")),
                    createdAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc"))
                });
            }
        }

        return CreatePageResult(connection, "dashboard_wallet_history", userId, items, normalizedPage, normalizedPageSize, offset);
    }

    public object GetWalletPurchases(string userId, int page, int pageSize)
    {
        var normalizedPage = NormalizePage(page);
        var normalizedPageSize = NormalizePageSize(pageSize);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var items = new List<object>();
        using var connection = _store.OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
SELECT checkout_id, target, pack_code, display_label, amount_minor, credits, premium_debt_credits_covered, status, client_confirmed, credited_at_utc, created_at_utc
FROM dashboard_wallet_purchases
WHERE user_id = @userId
ORDER BY created_at_utc DESC
OFFSET @offset
LIMIT @pageSize;";
            command.Parameters.AddWithValue("userId", userId);
            command.Parameters.AddWithValue("offset", offset);
            command.Parameters.AddWithValue("pageSize", normalizedPageSize);
            using var reader = command.ExecuteReader();
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
        }

        return CreatePageResult(connection, "dashboard_wallet_purchases", userId, items, normalizedPage, normalizedPageSize, offset);
    }

    public object GetDevices(string userId, int page, int pageSize)
    {
        var normalizedPage = NormalizePage(page);
        var normalizedPageSize = NormalizePageSize(pageSize);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var items = new List<object>();
        using var connection = _store.OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
SELECT
    device_install_id,
    device_fingerprint_hash,
    last_authenticated_at_utc,
    auth_method,
    is_active
FROM dashboard_device_inventory
WHERE user_id = @userId
ORDER BY last_authenticated_at_utc DESC
OFFSET @offset
LIMIT @pageSize;";
            command.Parameters.AddWithValue("userId", userId);
            command.Parameters.AddWithValue("offset", offset);
            command.Parameters.AddWithValue("pageSize", normalizedPageSize);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new
                {
                    deviceInstallId = reader.GetString(reader.GetOrdinal("device_install_id")),
                    deviceFingerprintHash = reader.GetString(reader.GetOrdinal("device_fingerprint_hash")),
                    lastAuthenticatedAtUtc = reader.GetDateTime(reader.GetOrdinal("last_authenticated_at_utc")),
                    authMethod = reader.GetString(reader.GetOrdinal("auth_method")),
                    isActive = reader.GetBoolean(reader.GetOrdinal("is_active"))
                });
            }
        }

        return CreatePageResult(connection, "dashboard_device_inventory", userId, items, normalizedPage, normalizedPageSize, offset);
    }

    public object GetDownloadEntitlement(string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT phone_verified, pro_available_credits, premium_available_credits
FROM dashboard_account_summaries
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
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT open_lock_session_id, last_usage_charge_credits, lease_expires_at_utc
FROM dashboard_support_previews
WHERE user_id = @userId
LIMIT 1;";
        command.Parameters.AddWithValue("userId", userId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("Account not found.");
        }

        var leaseExpiresAtUtc = reader.GetDateTime(reader.GetOrdinal("lease_expires_at_utc"));
        return new
        {
            openLockSessionId = reader.GetString(reader.GetOrdinal("open_lock_session_id")),
            lastUsageChargeCredits = reader.GetDecimal(reader.GetOrdinal("last_usage_charge_credits")),
            offlineLeaseHoursRemaining = Math.Round(Math.Max(0, (leaseExpiresAtUtc - DateTime.UtcNow).TotalHours), 1),
            supportMessage = "Support dashboards can inspect wallet state, stale locks, and recent usage without mutating runtime authority."
        };
    }

    public object GetInterviewQuestionBanks(string userId, int page, int pageSize)
    {
        var normalizedPage = NormalizePage(page);
        var normalizedPageSize = NormalizePageSize(pageSize);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var items = new List<object>();
        using var connection = _store.OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
SELECT session_id, interview_name, questions_json::text, interview_started_at_utc, interview_ended_at_utc
FROM dashboard_interview_question_banks
WHERE user_id = @userId
ORDER BY interview_ended_at_utc DESC
OFFSET @offset
LIMIT @pageSize;";
            command.Parameters.AddWithValue("userId", userId);
            command.Parameters.AddWithValue("offset", offset);
            command.Parameters.AddWithValue("pageSize", normalizedPageSize);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new
                {
                    sessionId = reader.GetString(reader.GetOrdinal("session_id")),
                    interviewName = reader.GetString(reader.GetOrdinal("interview_name")),
                    questions = System.Text.Json.JsonSerializer.Deserialize<string[]>(reader.GetString(reader.GetOrdinal("questions_json")))
                        ?? Array.Empty<string>(),
                    interviewStartedAtUtc = reader.GetDateTime(reader.GetOrdinal("interview_started_at_utc")),
                    interviewEndedAtUtc = reader.GetDateTime(reader.GetOrdinal("interview_ended_at_utc"))
                });
            }
        }

        return CreatePageResult(
            connection,
            "dashboard_interview_question_banks",
            userId,
            items,
            normalizedPage,
            normalizedPageSize,
            offset);
    }

    private static object CreatePageResult(
        Npgsql.NpgsqlConnection connection,
        string tableName,
        string userId,
        List<object> items,
        int page,
        int pageSize,
        int offset)
    {
        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE user_id = @userId;";
        countCommand.Parameters.AddWithValue("userId", userId);
        var totalCount = Convert.ToInt32(countCommand.ExecuteScalar() ?? 0);
        return new
        {
            items,
            page,
            pageSize,
            totalCount,
            hasNextPage = offset + items.Count < totalCount
        };
    }

    private static int NormalizePage(int page) => Math.Max(1, page);

    private static int NormalizePageSize(int pageSize) => Math.Clamp(pageSize, 1, 50);

}
