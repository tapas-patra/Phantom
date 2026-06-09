using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class AdminService
{
    private readonly AccountRepository _accounts;
    private readonly LockRepository _locks;
    private readonly UsageLedgerRepository _usageLedger;

    public AdminService(AccountRepository accounts, LockRepository locks, UsageLedgerRepository usageLedger)
    {
        _accounts = accounts;
        _locks = locks;
        _usageLedger = usageLedger;
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
}
