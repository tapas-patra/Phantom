namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class CompanionPairingCompleteResultDto
{
    public string PairingId { get; set; } = string.Empty;
    public string DesktopDeviceLabel { get; set; } = string.Empty;
    public string DesktopPlatform { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public bool RelayRequired { get; set; } = true;
}
