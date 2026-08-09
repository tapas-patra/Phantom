using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Services;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class UsageReconciliationService
{
    private const decimal ProtectedContinuationCap = 1.0m;
    private readonly PostgresBackendStore _store;
    private readonly UsageLedgerRepository _usageLedger;
    private readonly AccountRepository _accounts;
    private readonly InterviewQuestionBankJobRepository _questionBankJobs;

    public UsageReconciliationService(
        PostgresBackendStore store,
        UsageLedgerRepository usageLedger,
        AccountRepository accounts,
        InterviewQuestionBankJobRepository questionBankJobs)
    {
        _store = store;
        _usageLedger = usageLedger;
        _accounts = accounts;
        _questionBankJobs = questionBankJobs;
    }

    public UsageReconciliationResultDto Reconcile(UsageReconciliationRequestDto request, string authenticatedUserId)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new BackendValidationException("UserId and SessionId are required.");
        }

        if (!string.Equals(request.UserId, authenticatedUserId, StringComparison.Ordinal))
        {
            throw new BackendValidationException("Usage reconciliation user does not match the authenticated session.");
        }

        using var connection = _store.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var questionInputs = (request.QuestionInputs ?? new List<string>())
            .Where(input => !string.IsNullOrWhiteSpace(input))
            .Select(input => input.Trim()[..Math.Min(input.Trim().Length, 4000)])
            .Take(200)
            .ToArray();
        if (questionInputs.Length > 0)
        {
            _questionBankJobs.Enqueue(
                request.SessionId,
                request.UserId,
                questionInputs,
                request.StartedAtUtc,
                request.EndedAtUtc,
                connection,
                transaction);
        }

        var existing = _usageLedger.FindBySessionId(request.SessionId, connection, transaction);
        if (existing != null)
        {
            transaction.Commit();
            return new UsageReconciliationResultDto
            {
                Accepted = true,
                LedgerEntryId = existing.LedgerEntryId,
                AppliedCredits = existing.ChargedCredits - existing.AddedPremiumDebt,
                AddedPremiumDebt = existing.AddedPremiumDebt
            };
        }

        var account = _accounts.FindByUserId(request.UserId, connection, transaction, forUpdate: true)
            ?? throw new BackendValidationException("Account not found.");
        var isFreeTier = AccessModeResolver.IsFree(account);
        var existingDebt = Math.Max(0m, account.PremiumNegativeCredits);
        var remainingDebtBudget = Math.Max(0m, ProtectedContinuationCap - existingDebt);
        var requestedDebt = isFreeTier
            ? 0m
            : Math.Max(0m, Math.Min(request.PremiumDebtAdded, remainingDebtBudget));
        requestedDebt = Math.Min(requestedDebt, request.ChargedCredits);
        var requestedPaidCharge = Math.Max(0m, request.ChargedCredits - requestedDebt);
        var explicitProCharge = Math.Max(0m, request.ConsumedProCredits);
        var explicitPremiumCharge = Math.Max(0m, request.ConsumedPremiumCredits);
        var explicitPaidCharge = explicitProCharge + explicitPremiumCharge;
        var hasExplicitSplit = explicitPaidCharge > 0m || requestedPaidCharge == 0m;
        if (explicitPaidCharge > requestedPaidCharge)
        {
            throw new BackendValidationException("Usage reconciliation credit split exceeds the charged amount.");
        }

        var remainingCharge = requestedPaidCharge;
        var consumedProCredits = 0m;
        var consumedPremiumCredits = 0m;

        if (hasExplicitSplit)
        {
            if (!isFreeTier && explicitProCharge > 0m && account.ProAvailableCredits > 0m)
            {
                consumedProCredits = Math.Min(account.ProAvailableCredits, explicitProCharge);
                account.ProAvailableCredits -= consumedProCredits;
                remainingCharge -= consumedProCredits;
            }

            if (explicitPremiumCharge > 0m && account.PremiumAvailableCredits > 0m)
            {
                consumedPremiumCredits = Math.Min(account.PremiumAvailableCredits, explicitPremiumCharge);
                account.PremiumAvailableCredits -= consumedPremiumCredits;
                remainingCharge -= consumedPremiumCredits;
            }

            if (!isFreeTier && remainingCharge > 0m && account.ProAvailableCredits > 0m)
            {
                var fallbackPro = Math.Min(account.ProAvailableCredits, remainingCharge);
                account.ProAvailableCredits -= fallbackPro;
                consumedProCredits += fallbackPro;
                remainingCharge -= fallbackPro;
            }

            if (remainingCharge > 0m && account.PremiumAvailableCredits > 0m)
            {
                var fallbackPremium = Math.Min(account.PremiumAvailableCredits, remainingCharge);
                account.PremiumAvailableCredits -= fallbackPremium;
                consumedPremiumCredits += fallbackPremium;
                remainingCharge -= fallbackPremium;
            }
        }
        else
        {
            if (remainingCharge > 0m && account.PremiumAvailableCredits > 0m)
            {
                consumedPremiumCredits = Math.Min(account.PremiumAvailableCredits, remainingCharge);
                account.PremiumAvailableCredits -= consumedPremiumCredits;
                remainingCharge -= consumedPremiumCredits;
            }

            if (!isFreeTier && remainingCharge > 0m && account.ProAvailableCredits > 0m)
            {
                consumedProCredits = Math.Min(account.ProAvailableCredits, remainingCharge);
                account.ProAvailableCredits -= consumedProCredits;
                remainingCharge -= consumedProCredits;
            }
        }

        var appliedCredits = consumedProCredits + consumedPremiumCredits;

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
        account.AccessTier = AccessModeResolver.GetEffectiveAccessTier(account);
        account.LastValidatedAtUtc = DateTime.UtcNow;
        account.UpdatedAtUtc = DateTime.UtcNow;
        _accounts.Save(account, connection, transaction);

        var ledger = new UsageLedgerRecord
        {
            LedgerEntryId = $"ledger-{Guid.NewGuid():N}",
            UserId = request.UserId,
            SessionId = request.SessionId,
            StartedAtUtc = request.StartedAtUtc,
            EndedAtUtc = request.EndedAtUtc,
            ChargedCredits = request.ChargedCredits,
            ChargedBlocks = request.ChargedBlocks,
            ChargedProCredits = consumedProCredits,
            ChargedPremiumCredits = consumedPremiumCredits,
            AddedPremiumDebt = addedDebt,
            CreatedAtUtc = DateTime.UtcNow
        };
        _usageLedger.Save(ledger, connection, transaction);
        transaction.Commit();

        return new UsageReconciliationResultDto
        {
            Accepted = true,
            LedgerEntryId = ledger.LedgerEntryId,
            AppliedCredits = appliedCredits,
            AddedPremiumDebt = addedDebt
        };
    }
}
