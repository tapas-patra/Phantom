using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Hosted
{
    public sealed class HttpHostedLockClient : HttpHostedClientBase, IHostedLockClient
    {
        public HttpHostedLockClient(HostedRuntimeOptions options)
            : base(options)
        {
        }

        public DeviceLockAcquireResultDto Acquire(DeviceLockAcquireRequestDto request, string accessToken)
        {
            return PostJson<DeviceLockAcquireRequestDto, DeviceLockAcquireResultDto>(
                "/api/desktop/locks/acquire",
                request,
                accessToken);
        }

        public DeviceLockAcquireResultDto Heartbeat(DeviceLockHeartbeatRequestDto request, string accessToken)
        {
            return PostJson<DeviceLockHeartbeatRequestDto, DeviceLockAcquireResultDto>(
                "/api/desktop/locks/heartbeat",
                request,
                accessToken);
        }

        public void Release(DeviceLockReleaseRequestDto request, string accessToken)
        {
            PostJson<DeviceLockReleaseRequestDto, object>(
                "/api/desktop/locks/release",
                request,
                accessToken);
        }
    }
}
