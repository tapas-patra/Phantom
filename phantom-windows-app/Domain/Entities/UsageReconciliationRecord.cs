using System;
using SecureOverlay.Domain.Enums;

namespace SecureOverlay.Domain.Entities
{
    public sealed class UsageReconciliationRecord
    {
        public string RecordId { get; set; } = string.Empty;
        public UsageReconciliationPayload Payload { get; set; } = new UsageReconciliationPayload();
        public UsageSyncStatus Status { get; set; } = UsageSyncStatus.Pending;
        public int AttemptCount { get; set; }
        public string LastError { get; set; } = string.Empty;
        public string LedgerEntryId { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? LastAttemptAtUtc { get; set; }
        public DateTime? SyncedAtUtc { get; set; }
        public DateTime? DeadLetteredAtUtc { get; set; }
    }
}
