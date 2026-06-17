namespace Phantom.WindowsApp.Backend.Domain;

public sealed class DesktopLockRecord
{
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string LockToken { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime LastHeartbeatAtUtc { get; set; }
    public string AppVersion { get; set; } = string.Empty;
}
