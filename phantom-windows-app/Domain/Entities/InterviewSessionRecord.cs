using System;
using System.Collections.Generic;
using SecureOverlay.Domain.Enums;

namespace SecureOverlay.Domain.Entities
{
    public sealed class InterviewSessionRecord
    {
        public string SessionId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; }
        public DateTime LastHeartbeatAtUtc { get; set; }
        public DateTime? LockExpiresAtUtc { get; set; }
        public DateTime? EndedAtUtc { get; set; }
        public DateTime? PausedAtUtc { get; set; }
        public int TotalPausedSeconds { get; set; }
        public List<InterviewSessionUsageSegment> UsageSegments { get; set; } = new List<InterviewSessionUsageSegment>();
        public InterviewSessionState State { get; set; } = InterviewSessionState.Active;
        public CreditLedgerType PrimaryLedger { get; set; } = CreditLedgerType.Pro;
        public string LockTokenHash { get; set; } = string.Empty;
        public int HeartbeatIntervalSeconds { get; set; } = 60;
        public int LockTtlSeconds { get; set; } = 300;
        public decimal ChargedCredits { get; set; }
        public int ChargedBlocks { get; set; }
        public decimal PremiumDebtAdded { get; set; }
    }
}
