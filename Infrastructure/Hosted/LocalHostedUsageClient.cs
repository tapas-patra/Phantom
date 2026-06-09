using System;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Hosted
{
    public sealed class LocalHostedUsageClient : IHostedUsageClient
    {
        public UsageReconciliationResultDto Reconcile(UsageReconciliationRequestDto request)
        {
            return new UsageReconciliationResultDto
            {
                Accepted = true,
                LedgerEntryId = $"ledger-{request.SessionId}-{Guid.NewGuid():N}",
                AppliedCredits = request.ChargedCredits,
                AddedPremiumDebt = 0m
            };
        }
    }
}
