using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Hosted
{
    public interface IHostedLockClient
    {
        DeviceLockAcquireResultDto Acquire(DeviceLockAcquireRequestDto request, string accessToken);
        DeviceLockAcquireResultDto Heartbeat(DeviceLockHeartbeatRequestDto request, string accessToken);
        void Release(DeviceLockReleaseRequestDto request, string accessToken);
    }
}
