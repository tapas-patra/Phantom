using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class AdminService
{
    private readonly PostgresBackendStore _store;
    private readonly AccountRepository _accounts;
    private readonly LockRepository _locks;
    private readonly UsageLedgerRepository _usageLedger;
    private readonly PaymentOrderRepository _payments;

    public AdminService(
        PostgresBackendStore store,
        AccountRepository accounts,
        LockRepository locks,
        UsageLedgerRepository usageLedger,
        PaymentOrderRepository payments)
    {
        _store = store;
        _accounts = accounts;
        _locks = locks;
        _usageLedger = usageLedger;
        _payments = payments;
    }

    public AdminAccountSnapshotDto GetAccountSnapshot(string userId)
    {
        var account = _accounts.FindByUserId(userId)
            ?? throw new BackendValidationException("Account not found.");
        var activeLock = _locks.FindActiveByUser(userId);
        var recentEntries = _usageLedger.ListRecentForUser(userId, 20);

        return new AdminAccountSnapshotDto
        {
            UserId = account.UserId,
            Email = account.Email,
            PhoneVerified = account.PhoneVerified,
            ProAvailableCredits = account.ProAvailableCredits,
            PremiumAvailableCredits = account.PremiumAvailableCredits,
            PremiumNegativeCredits = account.PremiumNegativeCredits,
            LeaseExpiresAtUtc = account.LeaseExpiresAtUtc,
            OfflineModeEnabled = account.OfflineModeEnabled,
            LastValidatedAtUtc = account.LastValidatedAtUtc,
            ActiveLockSessionId = activeLock?.SessionId ?? string.Empty,
            ActiveLockDeviceId = activeLock?.DeviceId ?? string.Empty,
            ActiveLockExpiresAtUtc = activeLock?.ExpiresAtUtc,
            RecentLedgerEntries = recentEntries.Select(entry => new UsageLedgerSummaryDto
            {
                LedgerEntryId = entry.LedgerEntryId,
                SessionId = entry.SessionId,
                ChargedCredits = entry.ChargedCredits,
                ChargedBlocks = entry.ChargedBlocks,
                AddedPremiumDebt = entry.AddedPremiumDebt,
                CreatedAtUtc = entry.CreatedAtUtc
            }).ToList()
        };
    }

    public IReadOnlyList<object> ListAccounts()
    {
        return _accounts.ListAll()
            .Select(account => (object)new
            {
                account.UserId,
                account.Email,
                account.PhoneVerified,
                account.ProAvailableCredits,
                account.PremiumAvailableCredits,
                account.PremiumNegativeCredits,
                account.LeaseExpiresAtUtc,
                account.OfflineModeEnabled,
                account.LastValidatedAtUtc
            })
            .ToList();
    }

    public object GetOverview()
    {
        using var connection = _store.OpenConnection();
        return new
        {
            accountCount = ExecuteCount(connection, "SELECT COUNT(*) FROM desktop_accounts;"),
            activeSessionCount = ExecuteCount(connection, "SELECT COUNT(*) FROM auth_sessions WHERE is_authenticated = TRUE AND revoked_at_utc IS NULL;"),
            activeLockCount = ExecuteCount(connection, "SELECT COUNT(*) FROM interview_locks WHERE expires_at_utc > NOW();"),
            ledgerEntryCount = ExecuteCount(connection, "SELECT COUNT(*) FROM usage_ledger;"),
            managedCredentialCount = ExecuteCount(connection, "SELECT COUNT(*) FROM managed_provider_credentials;"),
            paymentOrderCount = ExecuteCount(connection, "SELECT COUNT(*) FROM payment_orders;"),
            creditedPaymentCount = ExecuteCount(connection, "SELECT COUNT(*) FROM payment_orders WHERE credited_at_utc IS NOT NULL;"),
            paymentWebhookCount = ExecuteCount(connection, "SELECT COUNT(*) FROM payment_webhook_events;"),
            processedWebhookCount = ExecuteCount(connection, "SELECT COUNT(*) FROM payment_webhook_events WHERE processed_at_utc IS NOT NULL;")
        };
    }

    public IReadOnlyList<object> GetPaymentOrders(int maxCount = 100)
    {
        return _payments.ListRecentOrders(maxCount)
            .Select(order => (object)new
            {
                order.CheckoutId,
                order.UserId,
                order.Email,
                order.Target,
                order.PackCode,
                order.DisplayLabel,
                order.Currency,
                order.AmountMinor,
                amountInr = order.AmountMinor / 100m,
                order.Credits,
                order.PremiumDebtCreditsCovered,
                order.RazorpayOrderId,
                order.RazorpayPaymentId,
                status = order.CreditedAtUtc.HasValue ? "credited" : order.Status,
                order.ClientConfirmed,
                order.CreditedAtUtc,
                order.CreatedAtUtc,
                order.UpdatedAtUtc
            })
            .ToList();
    }

    public IReadOnlyList<object> GetPaymentWebhookEvents(int maxCount = 100)
    {
        return _payments.ListRecentWebhookEvents(maxCount)
            .Select(eventRecord => (object)new
            {
                eventRecord.EventRecordId,
                eventRecord.ExternalEventId,
                eventRecord.EventType,
                eventRecord.PayloadJson,
                eventRecord.CreatedAtUtc,
                eventRecord.ProcessedAtUtc
            })
            .ToList();
    }

    public object ClearLock(AdminLockClearRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new BackendValidationException("UserId is required.");
        }

        var activeLock = _locks.FindActiveByUser(request.UserId);
        if (activeLock == null)
        {
            return new { cleared = false, message = "No active lock found." };
        }

        _locks.Delete(activeLock.SessionId);
        return new { cleared = true, sessionId = activeLock.SessionId, reason = request.Reason };
    }

    public object WaiveNegativePremiumBalance(AdminBalanceWaiverRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new BackendValidationException("UserId is required.");
        }

        var account = _accounts.FindByUserId(request.UserId)
            ?? throw new BackendValidationException("Account not found.");
        var before = account.PremiumNegativeCredits;
        account.PremiumNegativeCredits = 0m;
        account.AccessTier = AccessModeResolver.GetEffectiveAccessTier(account);
        account.LastValidatedAtUtc = DateTime.UtcNow;
        account.UpdatedAtUtc = DateTime.UtcNow;
        _accounts.Save(account);

        return new { waived = true, before, after = account.PremiumNegativeCredits, reason = request.Reason };
    }

    public object GrantCredits(AdminCreditGrantRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new BackendValidationException("UserId is required.");
        }

        var account = _accounts.FindByUserId(request.UserId)
            ?? throw new BackendValidationException("Account not found.");

        account.ProAvailableCredits += request.ProCreditsToAdd;
        account.PremiumAvailableCredits += request.PremiumCreditsToAdd;
        account.AccessTier = AccessModeResolver.GetEffectiveAccessTier(account);
        account.LastValidatedAtUtc = DateTime.UtcNow;
        account.UpdatedAtUtc = DateTime.UtcNow;
        _accounts.Save(account);

        return new
        {
            granted = true,
            request.ProCreditsToAdd,
            request.PremiumCreditsToAdd,
            request.Reason,
            account.ProAvailableCredits,
            account.PremiumAvailableCredits
        };
    }

    private static int ExecuteCount(Npgsql.NpgsqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }
}
