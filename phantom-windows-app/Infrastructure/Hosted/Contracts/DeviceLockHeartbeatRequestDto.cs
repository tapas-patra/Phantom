namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class DeviceLockHeartbeatRequestDto
    {
        public string SessionId { get; set; } = string.Empty;
        public string LockToken { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
    }
}
