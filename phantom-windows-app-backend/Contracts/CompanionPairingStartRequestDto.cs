namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class CompanionPairingStartRequestDto
{
    public string DesktopDeviceLabel { get; set; } = string.Empty;
    public string DesktopPlatform { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
}
