using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Services;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class UsageReconciliationService
{
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
        var remainingCharge = request.ChargedCredits;
        var appliedCredits = 0m;

        if (account.ProAvailableCredits > 0m)
        {
            var fromPro = Math.Min(account.ProAvailableCredits, remainingCharge);
            account.ProAvailableCredits -= fromPro;
            remainingCharge -= fromPro;
            appliedCredits += fromPro;
        }

        if (remainingCharge > 0m && account.PremiumAvailableCredits > 0m)
        {
            var fromPremium = Math.Min(account.PremiumAvailableCredits, remainingCharge);
            account.PremiumAvailableCredits -= fromPremium;
            remainingCharge -= fromPremium;
            appliedCredits += fromPremium;
        }

        var addedDebt = remainingCharge > 0m ? remainingCharge : 0m;
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
