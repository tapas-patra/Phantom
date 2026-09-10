namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class CompanionPairingDto
{
    public string PairingId { get; set; } = string.Empty;
    public string DesktopDeviceLabel { get; set; } = string.Empty;
    public string DesktopPlatform { get; set; } = string.Empty;
    public string CompanionDeviceLabel { get; set; } = string.Empty;
    public string CompanionPlatform { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public bool DesktopOnline { get; set; }
    public bool PhoneOnline { get; set; }
}
