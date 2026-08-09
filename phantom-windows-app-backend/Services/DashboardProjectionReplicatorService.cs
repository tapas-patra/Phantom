using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class DashboardProjectionReplicatorService : BackgroundService
{
    private const int BatchSize = 200;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly DashboardProjectionReplicaStore _replicaStore;
    private readonly PostgresBackendStore _primaryStore;
    private readonly OperationalMetricsService _metrics;
    private readonly ILogger<DashboardProjectionReplicatorService> _logger;

    public DashboardProjectionReplicatorService(
        DashboardProjectionReplicaStore replicaStore,
        PostgresBackendStore primaryStore,
        OperationalMetricsService metrics,
        ILogger<DashboardProjectionReplicatorService> logger)
    {
        _replicaStore = replicaStore;
        _primaryStore = primaryStore;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_replicaStore.IsEnabled)
        {
            _metrics.RecordProjectionMode(replicaEnabled: false);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    PurgeOutbox();
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _metrics.RecordProjectionFailure();
                    _logger.LogError(ex, "Dashboard projection outbox purge failed.");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }

            return;
        }

        _metrics.RecordProjectionMode(replicaEnabled: true);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var applied = ReplicateBatch();
                if (applied == 0)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _metrics.RecordProjectionFailure();
                _logger.LogError(ex, "Dashboard projection replication failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private int ReplicateBatch()
    {
        using var primaryConnection = _primaryStore.OpenConnection();
        using var primaryTransaction = primaryConnection.BeginTransaction();
        var changes = LoadChanges(primaryConnection, primaryTransaction);
        if (changes.Count == 0)
        {
            _metrics.RecordProjectionPending(0, oldestPendingAtUtc: null);
            primaryTransaction.Commit();
            return 0;
        }

        using var replicaConnection = _replicaStore.OpenConnection();
        using var replicaTransaction = replicaConnection.BeginTransaction();
        foreach (var change in changes)
        {
            ApplyChange(replicaConnection, replicaTransaction, change);
        }

        replicaTransaction.Commit();
        DeleteChanges(primaryConnection, primaryTransaction, changes);
        var pending = GetPendingState(primaryConnection, primaryTransaction);
        _metrics.RecordProjectionBatchApplied(changes.Count, pending.PendingCount, pending.OldestPendingAtUtc);
        primaryTransaction.Commit();
        return changes.Count;
    }

    private void PurgeOutbox()
    {
        using var connection = _primaryStore.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM dashboard_projection_outbox;";
        var deleted = command.ExecuteNonQuery();
        _metrics.RecordProjectionBatchApplied(deleted, 0, oldestPendingAtUtc: null);
    }

    private static PendingState GetPendingState(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
SELECT COUNT(*) AS pending_count, MIN(occurred_at_utc) AS oldest_pending_at_utc
FROM dashboard_projection_outbox;";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new PendingState(0, null);
        }

        return new PendingState(
            reader.GetInt64(reader.GetOrdinal("pending_count")),
            reader.IsDBNull(reader.GetOrdinal("oldest_pending_at_utc"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("oldest_pending_at_utc")));
    }

    private static List<ProjectionChange> LoadChanges(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
SELECT change_id, entity_type, operation, payload_json::text
FROM dashboard_projection_outbox
ORDER BY change_id ASC
LIMIT @batchSize
FOR UPDATE SKIP LOCKED;";
        command.Parameters.AddWithValue("batchSize", BatchSize);
        using var reader = command.ExecuteReader();
        var items = new List<ProjectionChange>();
        while (reader.Read())
        {
            items.Add(new ProjectionChange(
                reader.GetInt64(reader.GetOrdinal("change_id")),
                reader.GetString(reader.GetOrdinal("entity_type")),
                reader.GetString(reader.GetOrdinal("operation")),
                reader.GetString(reader.GetOrdinal("payload_json"))));
        }

        return items;
    }

    private static void DeleteChanges(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<ProjectionChange> changes)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM dashboard_projection_outbox WHERE change_id = ANY(@changeIds);";
        command.Parameters.AddWithValue("changeIds", changes.Select(change => change.ChangeId).ToArray());
        command.ExecuteNonQuery();
    }

    private static void ApplyChange(NpgsqlConnection connection, NpgsqlTransaction transaction, ProjectionChange change)
    {
        using var payload = JsonDocument.Parse(change.PayloadJson);
        var root = payload.RootElement;

        switch (change.EntityType)
        {
            case "dashboard_account_summaries":
                ApplyAccountSummary(connection, transaction, change.Operation, root);
                break;
            case "dashboard_wallet_history":
                ApplyWalletHistory(connection, transaction, change.Operation, root);
                break;
            case "dashboard_wallet_purchases":
                ApplyWalletPurchase(connection, transaction, change.Operation, root);
                break;
            case "dashboard_device_inventory":
                ApplyDeviceInventory(connection, transaction, change.Operation, root);
                break;
            case "dashboard_support_previews":
                ApplySupportPreview(connection, transaction, change.Operation, root);
                break;
            case "dashboard_interview_question_banks":
                ApplyInterviewQuestionBank(connection, transaction, change.Operation, root);
                break;
            default:
                throw new InvalidOperationException($"Unsupported dashboard projection entity '{change.EntityType}'.");
        }
    }

    private static void ApplyAccountSummary(NpgsqlConnection connection, NpgsqlTransaction transaction, string operation, JsonElement root)
    {
        if (operation == "DELETE")
        {
            ExecuteDelete(connection, transaction, "DELETE FROM dashboard_account_summaries WHERE user_id = @userId;",
                ("userId", root.GetProperty("user_id").GetString() ?? string.Empty));
            return;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO dashboard_account_summaries (
    user_id, email, effective_access_tier, plan_label, phone_verified,
    pro_available_credits, premium_available_credits, premium_negative_credits,
    lease_expires_at_utc, offline_mode_enabled, last_validated_at_utc,
    active_device_count, last_activity_at_utc, updated_at_utc
) VALUES (
    @userId, @email, @effectiveAccessTier, @planLabel, @phoneVerified,
    @proCredits, @premiumCredits, @premiumNegativeCredits, @leaseExpiresAtUtc,
    @offlineModeEnabled, @lastValidatedAtUtc, @activeDeviceCount, @lastActivityAtUtc, @updatedAtUtc
)
ON CONFLICT (user_id) DO UPDATE SET
    email = EXCLUDED.email,
    effective_access_tier = EXCLUDED.effective_access_tier,
    plan_label = EXCLUDED.plan_label,
    phone_verified = EXCLUDED.phone_verified,
    pro_available_credits = EXCLUDED.pro_available_credits,
    premium_available_credits = EXCLUDED.premium_available_credits,
    premium_negative_credits = EXCLUDED.premium_negative_credits,
    lease_expires_at_utc = EXCLUDED.lease_expires_at_utc,
    offline_mode_enabled = EXCLUDED.offline_mode_enabled,
    last_validated_at_utc = EXCLUDED.last_validated_at_utc,
    active_device_count = EXCLUDED.active_device_count,
    last_activity_at_utc = EXCLUDED.last_activity_at_utc,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        command.Parameters.AddWithValue("userId", root.GetProperty("user_id").GetString() ?? string.Empty);
        command.Parameters.AddWithValue("email", root.GetProperty("email").GetString() ?? string.Empty);
        command.Parameters.AddWithValue("effectiveAccessTier", root.GetProperty("effective_access_tier").GetString() ?? string.Empty);
        command.Parameters.AddWithValue("planLabel", root.GetProperty("plan_label").GetString() ?? string.Empty);
        command.Parameters.AddWithValue("phoneVerified", root.GetProperty("phone_verified").GetBoolean());
        command.Parameters.AddWithValue("proCredits", root.GetProperty("pro_available_credits").GetDecimal());
        command.Parameters.AddWithValue("premiumCredits", root.GetProperty("premium_available_credits").GetDecimal());
        command.Parameters.AddWithValue("premiumNegativeCredits", root.GetProperty("premium_negative_credits").GetDecimal());
        command.Parameters.AddWithValue("leaseExpiresAtUtc", root.GetProperty("lease_expires_at_utc").GetDateTime());
        command.Parameters.AddWithValue("offlineModeEnabled", root.GetProperty("offline_mode_enabled").GetBoolean());
        command.Parameters.AddWithValue("lastValidatedAtUtc", root.GetProperty("last_validated_at_utc").GetDateTime());
        command.Parameters.AddWithValue("activeDeviceCount", root.GetProperty("active_device_count").GetInt32());
        command.Parameters.AddWithValue("lastActivityAtUtc",
            root.TryGetProperty("last_activity_at_utc", out var lastActivity) && lastActivity.ValueKind != JsonValueKind.Null
                ? lastActivity.GetDateTime()
                : (object)DBNull.Value);
        command.Parameters.AddWithValue("updatedAtUtc", root.GetProperty("updated_at_utc").GetDateTime());
        command.ExecuteNonQuery();
    }

    private static void ApplyWalletHistory(NpgsqlConnection connection, NpgsqlTransaction transaction, string operation, JsonElement root)
    {
        if (operation == "DELETE")
        {
            ExecuteDelete(connection, transaction, "DELETE FROM dashboard_wallet_history WHERE ledger_entry_id = @ledgerEntryId;",
                ("ledgerEntryId", root.GetProperty("ledger_entry_id").GetString() ?? string.Empty));
            return;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO dashboard_wallet_history (
    ledger_entry_id, user_id, session_id, charged_credits, charged_blocks,
    charged_pro_credits, charged_premium_credits, added_premium_debt, created_at_utc
) VALUES (
    @ledgerEntryId, @userId, @sessionId, @chargedCredits, @chargedBlocks,
    @chargedProCredits, @chargedPremiumCredits, @addedPremiumDebt, @createdAtUtc
)
ON CONFLICT (ledger_entry_id) DO UPDATE SET
    user_id = EXCLUDED.user_id,
    session_id = EXCLUDED.session_id,
    charged_credits = EXCLUDED.charged_credits,
    charged_blocks = EXCLUDED.charged_blocks,
    charged_pro_credits = EXCLUDED.charged_pro_credits,
    charged_premium_credits = EXCLUDED.charged_premium_credits,
    added_premium_debt = EXCLUDED.added_premium_debt,
    created_at_utc = EXCLUDED.created_at_utc;";
        command.Parameters.AddWithValue("ledgerEntryId", root.GetProperty("ledger_entry_id").GetString() ?? string.Empty);
        command.Parameters.AddWithValue("userId", root.GetProperty("user_id").GetString() ?? string.Empty);
        command.Parameters.AddWithValue("sessionId", root.GetProperty("session_id").GetString() ?? string.Empty);
        command.Parameters.AddWithValue("chargedCredits", root.GetProperty("charged_credits").GetDecimal());
        command.Parameters.AddWithValue("chargedBlocks", root.GetProperty("charged_blocks").GetInt32());
        command.Parameters.AddWithValue("chargedProCredits", root.GetProperty("charged_pro_credits").GetDecimal());
        command.Parameters.AddWithValue("chargedPremiumCredits", root.GetProperty("charged_premium_credits").GetDecimal());
        command.Parameters.AddWithValue("addedPremiumDebt", root.GetProperty("added_premium_debt").GetDecimal());
        command.Parameters.AddWithValue("createdAtUtc", root.GetProperty("created_at_utc").GetDateTime());
        command.ExecuteNonQuery();
    }

    private static void ApplyWalletPurchase(NpgsqlConnection connection, NpgsqlTransaction transaction, string operation, JsonElement root)
    {
        if (operation == "DELETE")
        {
            ExecuteDelete(connection, transaction, "DELETE FROM dashboard_wallet_purchases WHERE checkout_id = @checkoutId;",
                ("checkoutId", root.GetProperty("checkout_id").GetString() ?? string.Empty));
            return;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO dashboard_wallet_purchases (
    checkout_id, user_id, target, pack_code, display_label, amount_minor, credits,
    premium_debt_credits_covered, status, client_confirmed, credited_at_utc, created_at_utc
) VALUES (
    @checkoutId, @userId, @target, @packCode, @displayLabel, @amountMinor, @credits,
    @premiumDebtCreditsCovered, @status, @clientConfirmed, @creditedAtUtc, @createdAtUtc
)
ON CONFLICT (checkout_id) DO UPDATE SET
    user_id = EXCLUDED.user_id,
    target = EXCLUDED.target,
    pack_code = EXCLUDED.pack_code,
    display_label = EXCLUDED.display_label,
    amount_minor = EXCLUDED.amount_minor,
    credits = EXCLUDED.credits,
    premium_debt_credits_covered = EXCLUDED.premium_debt_credits_covered,
    status = EXCLUDED.status,
    client_confirmed = EXCLUDED.client_confirmed,
    credited_at_utc = EXCLUDED.credited_at_utc,
    created_at_utc = EXCLUDED.created_at_utc;";
        command.Parameters.AddWithValue("checkoutId", root.GetProperty("checkout_id").GetString() ?? string.Empty);
        command.Parameters.AddWithValue("userId", root.GetProperty("user_id").GetString() ?? string.Empty);
        command.Parameters.AddWithValue("target", root.GetProperty("target").GetString() ?? string.Empty);
        command.Parameters.AddWithValue("packCode", root.GetProperty("pack_code").GetString() ?? string.Empty);
        command.Parameters.AddWithValue("displayLabel", root.GetProperty("display_label").GetString() ?? string.Empty);
        command.Parameters.AddWithValue("amountMinor", root.GetProperty("amount_minor").GetInt32());
        command.Parameters.AddWithValue("credits", root.GetProperty("credits").GetDecimal());
        command.Parameters.AddWithValue("premiumDebtCreditsCovered", root.GetProperty("premium_debt_credits_covered").GetDecimal());
        command.Parameters.AddWithValue("status", root.GetProperty("status").GetString() ?? string.Empty);
        command.Parameters.AddWithValue("clientConfirmed", root.GetProperty("client_confirmed").GetBoolean());
        command.Parameters.AddWithValue("creditedAtUtc",
            root.TryGetProperty("credited_at_utc", out var creditedAt) && creditedAt.ValueKind != JsonValueKind.Null
                ? creditedAt.GetDateTime()
                : (object)DBNull.Value);
        command.Parameters.AddWithValue("createdAtUtc", root.GetProperty("created_at_utc").GetDateTime());
        command.ExecuteNonQuery();
    }

    private static void ApplyDeviceInventory(NpgsqlConnection connection, NpgsqlTransaction transaction, string operation, JsonElement root)
    {
        if (operation == "DELETE")
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
DELETE FROM dashboard_device_inventory
WHERE user_id = @userId
  AND device_install_id = @deviceInstallId
  AND device_fingerprint_hash = @deviceFingerprintHash;";
            command.Parameters.AddWithValue("userId", root.GetProperty("user_id").GetString() ?? string.Empty);
            command.Parameters.AddWithValue("deviceInstallId", root.GetProperty("device_install_id").GetString() ?? string.Empty);
            command.Parameters.AddWithValue("deviceFingerprintHash", root.GetProperty("device_fingerprint_hash").GetString() ?? string.Empty);
            command.ExecuteNonQuery();
            return;
        }

        using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = @"
INSERT INTO dashboard_device_inventory (
    user_id, device_install_id, device_fingerprint_hash, last_authenticated_at_utc, auth_method, is_active
) VALUES (
    @userId, @deviceInstallId, @deviceFingerprintHash, @lastAuthenticatedAtUtc, @authMethod, @isActive
)
ON CONFLICT (user_id, device_install_id, device_fingerprint_hash) DO UPDATE SET
    last_authenticated_at_utc = EXCLUDED.last_authenticated_at_utc,
    auth_method = EXCLUDED.auth_method,
    is_active = EXCLUDED.is_active;";
        upsert.Parameters.AddWithValue("userId", root.GetProperty("user_id").GetString() ?? string.Empty);
        upsert.Parameters.AddWithValue("deviceInstallId", root.GetProperty("device_install_id").GetString() ?? string.Empty);
        upsert.Parameters.AddWithValue("deviceFingerprintHash", root.GetProperty("device_fingerprint_hash").GetString() ?? string.Empty);
        upsert.Parameters.AddWithValue("lastAuthenticatedAtUtc", root.GetProperty("last_authenticated_at_utc").GetDateTime());
        upsert.Parameters.AddWithValue("authMethod", root.GetProperty("auth_method").GetString() ?? string.Empty);
        upsert.Parameters.AddWithValue("isActive", root.GetProperty("is_active").GetBoolean());
        upsert.ExecuteNonQuery();
    }

    private static void ApplySupportPreview(NpgsqlConnection connection, NpgsqlTransaction transaction, string operation, JsonElement root)
    {
        if (operation == "DELETE")
        {
            ExecuteDelete(connection, transaction, "DELETE FROM dashboard_support_previews WHERE user_id = @userId;",
                ("userId", root.GetProperty("user_id").GetString() ?? string.Empty));
            return;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO dashboard_support_previews (
    user_id, open_lock_session_id, last_usage_charge_credits, lease_expires_at_utc, updated_at_utc
) VALUES (
    @userId, @openLockSessionId, @lastUsageChargeCredits, @leaseExpiresAtUtc, @updatedAtUtc
)
ON CONFLICT (user_id) DO UPDATE SET
    open_lock_session_id = EXCLUDED.open_lock_session_id,
    last_usage_charge_credits = EXCLUDED.last_usage_charge_credits,
    lease_expires_at_utc = EXCLUDED.lease_expires_at_utc,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        command.Parameters.AddWithValue("userId", root.GetProperty("user_id").GetString() ?? string.Empty);
        command.Parameters.AddWithValue("openLockSessionId", root.GetProperty("open_lock_session_id").GetString() ?? string.Empty);
        command.Parameters.AddWithValue("lastUsageChargeCredits", root.GetProperty("last_usage_charge_credits").GetDecimal());
        command.Parameters.AddWithValue("leaseExpiresAtUtc", root.GetProperty("lease_expires_at_utc").GetDateTime());
        command.Parameters.AddWithValue("updatedAtUtc", root.GetProperty("updated_at_utc").GetDateTime());
        command.ExecuteNonQuery();
    }

    private static void ApplyInterviewQuestionBank(NpgsqlConnection connection, NpgsqlTransaction transaction, string operation, JsonElement root)
    {
        if (operation == "DELETE")
        {
            ExecuteDelete(connection, transaction, "DELETE FROM dashboard_interview_question_banks WHERE session_id = @sessionId;",
                ("sessionId", root.GetProperty("session_id").GetString() ?? string.Empty));
            return;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO dashboard_interview_question_banks (
    session_id, user_id, interview_name, questions_json,
    interview_started_at_utc, interview_ended_at_utc, created_at_utc
) VALUES (
    @sessionId, @userId, @interviewName, @questions,
    @interviewStartedAtUtc, @interviewEndedAtUtc, @createdAtUtc
)
ON CONFLICT (session_id) DO UPDATE SET
    user_id = EXCLUDED.user_id,
    interview_name = EXCLUDED.interview_name,
    questions_json = EXCLUDED.questions_json,
    interview_started_at_utc = EXCLUDED.interview_started_at_utc,
    interview_ended_at_utc = EXCLUDED.interview_ended_at_utc;";
        command.Parameters.AddWithValue("sessionId", root.GetProperty("session_id").GetString() ?? string.Empty);
        command.Parameters.AddWithValue("userId", root.GetProperty("user_id").GetString() ?? string.Empty);
        command.Parameters.AddWithValue(
            "interviewName",
            root.TryGetProperty("interview_name", out var interviewName)
                ? interviewName.GetString() ?? string.Empty
                : string.Empty);
        command.Parameters.AddWithValue("questions", NpgsqlDbType.Jsonb, root.GetProperty("questions_json").GetRawText());
        command.Parameters.AddWithValue("interviewStartedAtUtc", root.GetProperty("interview_started_at_utc").GetDateTime());
        command.Parameters.AddWithValue("interviewEndedAtUtc", root.GetProperty("interview_ended_at_utc").GetDateTime());
        command.Parameters.AddWithValue("createdAtUtc", root.GetProperty("created_at_utc").GetDateTime());
        command.ExecuteNonQuery();
    }

    private static void ExecuteDelete(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        (string Name, object Value) parameter)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        command.ExecuteNonQuery();
    }

    private sealed record ProjectionChange(long ChangeId, string EntityType, string Operation, string PayloadJson);
    private sealed record PendingState(long PendingCount, DateTime? OldestPendingAtUtc);
}
