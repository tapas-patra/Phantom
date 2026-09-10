namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class ManagedAiModelOptionDto
    {
        public string ModelId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool SupportsVision { get; set; }
        public bool EligibleForChat { get; set; } = true;
    }
}
