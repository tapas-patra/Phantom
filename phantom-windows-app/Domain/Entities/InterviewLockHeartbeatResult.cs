using System;

namespace SecureOverlay.Domain.Entities
{
    public sealed class InterviewLockHeartbeatResult
    {
        public bool Succeeded { get; set; }
        public bool Resumed { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime? LockExpiresAtUtc { get; set; }
    }
}
