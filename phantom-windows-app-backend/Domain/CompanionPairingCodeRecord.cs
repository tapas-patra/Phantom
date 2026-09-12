namespace Phantom.WindowsApp.Backend.Domain;

public sealed class CompanionPairingCodeRecord
{
    public string CodeHash { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DesktopDeviceId { get; set; } = string.Empty;
    public string DesktopDeviceLabel { get; set; } = string.Empty;
    public string DesktopPlatform { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
}
