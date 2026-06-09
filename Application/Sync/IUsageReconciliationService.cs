namespace SecureOverlay.Application.Sync
{
    public interface IUsageReconciliationService
    {
        void Enqueue(UsageReconciliationPayload payload);
        UsageReconciliationFlushResult FlushPending();
        UsageReconciliationQueueSnapshot GetQueueSnapshot();
    }
}
