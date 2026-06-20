using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Hosted
{
    public sealed class HttpHostedUsageClient : HttpHostedClientBase, IHostedUsageClient
    {
        public HttpHostedUsageClient(HostedRuntimeOptions options)
            : base(options)
        {
        }

        public UsageReconciliationResultDto Reconcile(UsageReconciliationRequestDto request, string accessToken)
        {
            return PostJson<UsageReconciliationRequestDto, UsageReconciliationResultDto>(
                "/api/desktop/usage/reconcile",
                request,
                accessToken);
        }
    }
}
