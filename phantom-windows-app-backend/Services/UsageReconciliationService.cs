using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Services;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class UsageReconciliationService
{
    private const decimal ProtectedContinuationCap = 1.0m;
    private readonly UsageLedgerRepository _usageLedger;
    private readonly AccountStateService _accounts;

    public UsageReconciliationService(UsageLedgerRepository usageLedger, AccountStateService accounts)
    {
        _usageLedger = usageLedger;
        _accounts = accounts;
    }

    public UsageReconciliationResultDto Reconcile(UsageReconciliationRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new BackendValidationException("UserId and SessionId are required.");
        }

        var existing = _usageLedger.FindBySessionId(request.SessionId);
        if (existing != null)
        {
            return new UsageReconciliationResultDto
            {
                Accepted = true,
                LedgerEntryId = existing.LedgerEntryId,
                AppliedCredits = existing.ChargedCredits - existing.AddedPremiumDebt,
                AddedPremiumDebt = existing.AddedPremiumDebt
            };
        }

        var account = _accounts.RequireAccount(request.UserId);
        var isFreeTier = string.Equals(account.AccessTier, "free", StringComparison.OrdinalIgnoreCase);
        var existingDebt = Math.Max(0m, account.PremiumNegativeCredits);
        var remainingDebtBudget = Math.Max(0m, ProtectedContinuationCap - existingDebt);
        var requestedDebt = isFreeTier
            ? 0m
            : Math.Max(0m, Math.Min(request.PremiumDebtAdded, remainingDebtBudget));
        requestedDebt = Math.Min(requestedDebt, request.ChargedCredits);
        var remainingCharge = Math.Max(0m, request.ChargedCredits - requestedDebt);
        var appliedCredits = 0m;

        if (remainingCharge > 0m && account.PremiumAvailableCredits > 0m)
        {
            var fromPremium = Math.Min(account.PremiumAvailableCredits, remainingCharge);
            account.PremiumAvailableCredits -= fromPremium;
            remainingCharge -= fromPremium;
            appliedCredits += fromPremium;
        }

        if (!isFreeTier && remainingCharge > 0m && account.ProAvailableCredits > 0m)
        {
            var fromPro = Math.Min(account.ProAvailableCredits, remainingCharge);
            account.ProAvailableCredits -= fromPro;
            remainingCharge -= fromPro;
            appliedCredits += fromPro;
        }

        var addedDebt = !isFreeTier ? requestedDebt : 0m;

        if (!isFreeTier && remainingCharge > 0m)
        {
            var overflowDebtBudget = Math.Max(0m, remainingDebtBudget - addedDebt);
            if (overflowDebtBudget > 0m)
            {
                var overflowDebt = Math.Min(remainingCharge, overflowDebtBudget);
                addedDebt += overflowDebt;
                remainingCharge -= overflowDebt;
            }
        }

        if (remainingCharge > 0m)
        {
            throw new BackendValidationException("Insufficient credit to reconcile this session.");
        }

        account.PremiumNegativeCredits += addedDebt;
        _accounts.Save(account);

        var ledger = new UsageLedgerRecord
        {
            LedgerEntryId = $"ledger-{Guid.NewGuid():N}",
            UserId = request.UserId,
            SessionId = request.SessionId,
            StartedAtUtc = request.StartedAtUtc,
            EndedAtUtc = request.EndedAtUtc,
            ChargedCredits = request.ChargedCredits,
            ChargedBlocks = request.ChargedBlocks,
            AddedPremiumDebt = addedDebt,
            CreatedAtUtc = DateTime.UtcNow
        };
        _usageLedger.Save(ledger);

        return new UsageReconciliationResultDto
        {
            Accepted = true,
            LedgerEntryId = ledger.LedgerEntryId,
            AppliedCredits = appliedCredits,
            AddedPremiumDebt = addedDebt
        };
    }
}
