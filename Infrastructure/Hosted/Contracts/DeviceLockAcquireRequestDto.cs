namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class DeviceLockAcquireRequestDto
    {
        public string UserId { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
    }
}
