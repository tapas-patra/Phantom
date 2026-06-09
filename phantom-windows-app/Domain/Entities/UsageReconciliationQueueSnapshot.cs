using System.Collections.Generic;

namespace SecureOverlay.Domain.Entities
{
    public sealed class UsageReconciliationQueueSnapshot
    {
        public int PendingCount { get; set; }
        public int FailedCount { get; set; }
        public int DeadLetterCount { get; set; }
        public IReadOnlyList<UsageReconciliationRecord> DeadLetters { get; set; } = new List<UsageReconciliationRecord>();
    }
}
