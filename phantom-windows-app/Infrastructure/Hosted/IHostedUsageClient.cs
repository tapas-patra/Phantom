using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Hosted
{
    public interface IHostedUsageClient
    {
        UsageReconciliationResultDto Reconcile(UsageReconciliationRequestDto request, string accessToken);
    }
}
