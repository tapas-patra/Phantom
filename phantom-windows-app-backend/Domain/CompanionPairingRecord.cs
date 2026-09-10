namespace Phantom.WindowsApp.Backend.Domain;

public sealed class CompanionPairingRecord
{
    public string PairingId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DesktopDeviceId { get; set; } = string.Empty;
    public string DesktopDeviceLabel { get; set; } = string.Empty;
    public string DesktopPlatform { get; set; } = string.Empty;
    public string CompanionDeviceId { get; set; } = string.Empty;
    public string CompanionDeviceLabel { get; set; } = string.Empty;
    public string CompanionPlatform { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PairedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
