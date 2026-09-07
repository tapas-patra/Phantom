using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class AdminService
{
    private const decimal MaximumCreditAdjustment = 10_000m;
    private const decimal MaximumAccountBalance = 100_000m;
    private readonly PostgresBackendStore _store;
    private readonly AccountRepository _accounts;
    private readonly LockRepository _locks;
    private readonly AuthSessionRepository _sessions;
    private readonly UsageLedgerRepository _usageLedger;
    private readonly PaymentOrderRepository _payments;

    public AdminService(
        PostgresBackendStore store,
        AccountRepository accounts,
        LockRepository locks,
        AuthSessionRepository sessions,
        UsageLedgerRepository usageLedger,
        PaymentOrderRepository payments)
    {
        _store = store;
        _accounts = accounts;
        _locks = locks;
        _sessions = sessions;
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
            CanUseDesktopPowerFeatures = account.CanUseDesktopPowerFeatures,
            IsManualLockActive = account.IsManualLockActive,
            ManualLockExpiresAtUtc = account.ManualLockExpiresAtUtc,
            ManualLockReason = account.ManualLockReason,
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
                account.CanUseDesktopPowerFeatures,
                isManualLockActive = account.IsManualLockActive,
                account.ManualLockExpiresAtUtc,
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
        RequireReason(request.Reason);
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

        if (request.ProAvailableCredits > MaximumAccountBalance
            || request.PremiumAvailableCredits > MaximumAccountBalance
            || request.PremiumNegativeCredits > MaximumAccountBalance)
        {
            throw new BackendValidationException($"Account credit values cannot exceed {MaximumAccountBalance:0}.");
        }

        var account = _accounts.FindByUserId(request.UserId)
            ?? throw new BackendValidationException("Account not found.");

        account.AccessTier = normalizedTier;
        account.ProAvailableCredits = request.ProAvailableCredits;
        account.PremiumAvailableCredits = request.PremiumAvailableCredits;
        account.PremiumNegativeCredits = request.PremiumNegativeCredits;
        account.OfflineModeEnabled = request.OfflineModeEnabled;
        account.CanUseDesktopPowerFeatures = request.CanUseDesktopPowerFeatures;
        account.LastValidatedAtUtc = DateTime.UtcNow;
        account.UpdatedAtUtc = DateTime.UtcNow;
        _accounts.Save(account);

        return GetAccountSnapshot(account.UserId);
    }

    public AdminAccountSnapshotDto SetManualLock(AdminManualLockRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new BackendValidationException("UserId is required.");
        }

        if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc.Value <= DateTime.UtcNow)
        {
            throw new BackendValidationException("Temporary lock expiry must be in the future.");
        }

        RequireReason(request.Reason);

        if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc.Value > DateTime.UtcNow.AddDays(365))
        {
            throw new BackendValidationException("Temporary lock expiry cannot be more than one year in the future.");
        }

        var account = _accounts.FindByUserId(request.UserId)
            ?? throw new BackendValidationException("Account not found.");
        account.ManualLockExpiresAtUtc = request.ExpiresAtUtc?.ToUniversalTime();
        account.ManualLockReason = request.ExpiresAtUtc.HasValue ? request.Reason.Trim() : string.Empty;
        account.UpdatedAtUtc = DateTime.UtcNow;
        _accounts.Save(account);

        if (account.IsManualLockActive)
        {
            _sessions.RevokeAllByUserId(account.UserId);
        }

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
            supportTicketCount = ExecuteCount(connection, "SELECT COUNT(*) FROM support_tickets;"),
            downloadCount = ExecuteCount(connection, "SELECT COUNT(*) FROM download_events;"),
            windowsDownloadCount = ExecuteCount(connection, "SELECT COUNT(*) FROM download_events WHERE platform = 'windows';"),
            macosDownloadCount = ExecuteCount(connection, "SELECT COUNT(*) FROM download_events WHERE platform = 'macos';"),
            uniqueDownloaderCount = ExecuteCount(connection, "SELECT COUNT(DISTINCT user_id) FROM download_events;"),
            downloadsLast30Days = ExecuteCount(connection, "SELECT COUNT(*) FROM download_events WHERE downloaded_at_utc >= NOW() - INTERVAL '30 days';"),
            feedbackCount = ExecuteCount(connection, "SELECT COUNT(*) FROM feedback_submissions;"),
            publishedReviewCount = ExecuteCount(connection, "SELECT COUNT(*) FROM feedback_submissions WHERE status = 'published';")
        };
    }

    public object GetPaymentOrders(int page = 1, int pageSize = 25, string query = "", string status = "")
    {
        var normalizedPage = NormalizePage(page);
        var normalizedPageSize = NormalizePageSize(pageSize);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var normalizedQuery = query?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedStatus = status?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedStatus == "all") normalizedStatus = string.Empty;
        var supportedStatuses = new[] { "", "created", "client_confirmed", "credited", "failed" };
        if (!supportedStatuses.Contains(normalizedStatus, StringComparer.Ordinal))
        {
            throw new BackendValidationException("Unsupported payment status filter.");
        }

        var items = _payments.ListOrdersPage(offset, normalizedPageSize, normalizedQuery, normalizedStatus)
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
        var totalCount = _payments.CountOrders(normalizedQuery, normalizedStatus);
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
                entry.ChargedProCredits,
                entry.ChargedPremiumCredits,
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
        RequireReason(request.Reason);

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
        RequireReason(request.Reason);

        var account = _accounts.FindByUserId(request.UserId)
            ?? throw new BackendValidationException("Account not found.");
        if (account.PremiumNegativeCredits <= 0m)
        {
            throw new BackendValidationException("This account has no Premium debt to waive.");
        }
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
        RequireReason(request.Reason);
        if (request.ProCreditsToAdd < 0m || request.PremiumCreditsToAdd < 0m)
        {
            throw new BackendValidationException("Credit grants cannot be negative.");
        }

        if (request.ProCreditsToAdd == 0m && request.PremiumCreditsToAdd == 0m)
        {
            throw new BackendValidationException("Enter at least one positive credit amount.");
        }

        if (request.ProCreditsToAdd > MaximumCreditAdjustment || request.PremiumCreditsToAdd > MaximumCreditAdjustment)
        {
            throw new BackendValidationException($"A single credit grant cannot exceed {MaximumCreditAdjustment:0} per wallet.");
        }

        var account = _accounts.FindByUserId(request.UserId)
            ?? throw new BackendValidationException("Account not found.");

        if (account.ProAvailableCredits + request.ProCreditsToAdd > MaximumAccountBalance
            || account.PremiumAvailableCredits + request.PremiumCreditsToAdd > MaximumAccountBalance)
        {
            throw new BackendValidationException($"The resulting account balance cannot exceed {MaximumAccountBalance:0}.");
        }

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

    private static void RequireReason(string reason)
    {
        var normalizedReason = reason?.Trim() ?? string.Empty;
        if (normalizedReason.Length < 8)
        {
            throw new BackendValidationException("Provide an operational reason of at least 8 characters.");
        }

        if (normalizedReason.Length > 500)
        {
            throw new BackendValidationException("Operational reason cannot exceed 500 characters.");
        }
    }

    private static int NormalizePage(int page) => Math.Max(1, page);

    private static int NormalizePageSize(int pageSize) => Math.Clamp(pageSize, 1, 50);
}
