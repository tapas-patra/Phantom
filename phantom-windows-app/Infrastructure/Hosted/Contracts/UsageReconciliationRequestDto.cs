using System;

namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class UsageReconciliationRequestDto
    {
        public string UserId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; }
        public DateTime EndedAtUtc { get; set; }
        public decimal ChargedCredits { get; set; }
        public int ChargedBlocks { get; set; }
        public decimal PremiumDebtAdded { get; set; }
    }
}
