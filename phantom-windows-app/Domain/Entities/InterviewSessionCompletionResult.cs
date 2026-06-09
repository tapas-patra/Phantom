namespace SecureOverlay.Domain.Entities
{
    public sealed class InterviewSessionCompletionResult
    {
        public string UserId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public System.DateTime StartedAtUtc { get; set; }
        public System.DateTime EndedAtUtc { get; set; }
        public decimal ChargedCredits { get; set; }
        public int ChargedBlocks { get; set; }
        public decimal PremiumDebtAdded { get; set; }
        public decimal RemainingProCredits { get; set; }
        public decimal RemainingPremiumCredits { get; set; }
    }
}
