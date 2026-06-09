namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class UsageReconciliationResultDto
    {
        public bool Accepted { get; set; }
        public string LedgerEntryId { get; set; } = string.Empty;
        public decimal AppliedCredits { get; set; }
        public decimal AddedPremiumDebt { get; set; }
    }
}
