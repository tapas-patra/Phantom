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
            EmailVerified = account.EmailVerified,
            PhoneVerified = account.PhoneVerified,
            AccessTier = account.AccessTier,
            PlanLabel = AccessModeResolver.GetPlanLabel(account),
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

    public object ListAccounts(string query, int page, int pageSize)
    {
        var normalizedQuery = query?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedPage = NormalizePage(page);
        var normalizedPageSize = NormalizePageSize(pageSize);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var items = _accounts.ListPage(normalizedQuery, offset, normalizedPageSize)
            .Select(account => (object)new
            {
                account.UserId,
                account.Email,
                account.EmailVerified,
                account.PhoneVerified,
                account.AccessTier,
                planLabel = AccessModeResolver.GetPlanLabel(account),
                account.ProAvailableCredits,
                account.PremiumAvailableCredits,
                account.PremiumNegativeCredits,
                account.LeaseExpiresAtUtc,
                account.OfflineModeEnabled,
                account.LastValidatedAtUtc
            })
            .ToList();
        var totalCount = _accounts.CountPage(normalizedQuery);
        return new
        {
            items,
            page = normalizedPage,
            pageSize = normalizedPageSize,
            totalCount,
            hasNextPage = offset + items.Count < totalCount
        };
    }

    public AdminAccountSnapshotDto UpdateAccount(AdminAccountUpdateRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new BackendValidationException("UserId is required.");
        }

        var normalizedTier = NormalizeAccessTier(request.AccessTier);
        if (request.ProAvailableCredits < 0m)
        {
            throw new BackendValidationException("Pro credits cannot be negative.");
        }

        if (request.PremiumAvailableCredits < 0m)
        {
            throw new BackendValidationException("Premium credits cannot be negative.");
        }

        if (request.PremiumNegativeCredits < 0m)
        {
            throw new BackendValidationException("Premium negative credits cannot be negative.");
        }

        var account = _accounts.FindByUserId(request.UserId)
            ?? throw new BackendValidationException("Account not found.");

        account.AccessTier = normalizedTier;
        account.ProAvailableCredits = request.ProAvailableCredits;
        account.PremiumAvailableCredits = request.PremiumAvailableCredits;
        account.PremiumNegativeCredits = request.PremiumNegativeCredits;
        account.OfflineModeEnabled = request.OfflineModeEnabled;
        account.LastValidatedAtUtc = DateTime.UtcNow;
        account.UpdatedAtUtc = DateTime.UtcNow;
        _accounts.Save(account);

        return GetAccountSnapshot(account.UserId);
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
            processedWebhookCount = ExecuteCount(connection, "SELECT COUNT(*) FROM payment_webhook_events WHERE processed_at_utc IS NOT NULL;"),
            openSupportTicketCount = ExecuteCount(connection, "SELECT COUNT(*) FROM support_tickets WHERE status IN ('open', 'investigating', 'waiting_for_user');"),
            supportTicketCount = ExecuteCount(connection, "SELECT COUNT(*) FROM support_tickets;")
        };
    }

    public object GetPaymentOrders(int page = 1, int pageSize = 25)
    {
        var normalizedPage = NormalizePage(page);
        var normalizedPageSize = NormalizePageSize(pageSize);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var items = _payments.ListOrdersPage(offset, normalizedPageSize)
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
        var totalCount = _payments.CountOrders();
        return new
        {
            items,
            page = normalizedPage,
            pageSize = normalizedPageSize,
            totalCount,
            hasNextPage = offset + items.Count < totalCount
        };
    }

    public object GetPaymentWebhookEvents(int page = 1, int pageSize = 25)
    {
        var normalizedPage = NormalizePage(page);
        var normalizedPageSize = NormalizePageSize(pageSize);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var items = _payments.ListWebhookEventsPage(offset, normalizedPageSize)
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
        var totalCount = _payments.CountWebhookEvents();
        return new
        {
            items,
            page = normalizedPage,
            pageSize = normalizedPageSize,
            totalCount,
            hasNextPage = offset + items.Count < totalCount
        };
    }

    public object GetLedgerEntries(string userId, int page = 1, int pageSize = 25)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new BackendValidationException("UserId is required.");
        }

        var normalizedPage = NormalizePage(page);
        var normalizedPageSize = NormalizePageSize(pageSize);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var items = _usageLedger.ListPageForUser(userId, offset, normalizedPageSize)
            .Select(entry => (object)new
            {
                entry.LedgerEntryId,
                entry.SessionId,
                entry.ChargedCredits,
                entry.ChargedBlocks,
                entry.AddedPremiumDebt,
                entry.CreatedAtUtc
            })
            .ToList();
        var totalCount = _usageLedger.CountForUser(userId);
        return new
        {
            items,
            page = normalizedPage,
            pageSize = normalizedPageSize,
            totalCount,
            hasNextPage = offset + items.Count < totalCount
        };
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

    private static string NormalizeAccessTier(string accessTier)
    {
        if (string.Equals(accessTier, AccessModeResolver.Free, StringComparison.OrdinalIgnoreCase))
        {
            return AccessModeResolver.Free;
        }

        if (string.Equals(accessTier, AccessModeResolver.ProByo, StringComparison.OrdinalIgnoreCase))
        {
            return AccessModeResolver.ProByo;
        }

        if (string.Equals(accessTier, AccessModeResolver.Premium, StringComparison.OrdinalIgnoreCase))
        {
            return AccessModeResolver.Premium;
        }

        throw new BackendValidationException("Unsupported access tier.");
    }

    private static int NormalizePage(int page) => Math.Max(1, page);

    private static int NormalizePageSize(int pageSize) => Math.Clamp(pageSize, 1, 50);
}
