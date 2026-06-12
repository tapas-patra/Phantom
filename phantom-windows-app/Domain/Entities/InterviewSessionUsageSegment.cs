using SecureOverlay.Domain.Enums;

namespace SecureOverlay.Domain.Entities
{
    public sealed class InterviewSessionUsageSegment
    {
        public InterviewUsageSource Source { get; set; } = InterviewUsageSource.ProByo;
        public string ProviderId { get; set; } = string.Empty;
        public int StartedMeteredSecond { get; set; }
        public int? EndedMeteredSecond { get; set; }
    }
}
