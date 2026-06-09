namespace SecureOverlay.Domain.Entities
{
    public sealed class InterviewSessionActivationResult
    {
        public bool Allowed { get; set; }
        public bool StartedNewSession { get; set; }
        public bool ResumedExistingSession { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public InterviewSessionRecord? Session { get; set; }
    }
}
