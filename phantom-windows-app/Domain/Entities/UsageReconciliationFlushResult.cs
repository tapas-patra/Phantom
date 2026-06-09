namespace SecureOverlay.Domain.Entities
{
    public sealed class UsageReconciliationFlushResult
    {
        public int PendingBefore { get; set; }
        public int SyncedCount { get; set; }
        public int FailedCount { get; set; }
    }
}
