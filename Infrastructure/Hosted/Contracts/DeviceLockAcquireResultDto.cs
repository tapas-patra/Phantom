using System;

namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class DeviceLockAcquireResultDto
    {
        public bool Acquired { get; set; }
        public string LockToken { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
        public string HolderDeviceId { get; set; } = string.Empty;
        public string HolderSessionId { get; set; } = string.Empty;
    }
}
