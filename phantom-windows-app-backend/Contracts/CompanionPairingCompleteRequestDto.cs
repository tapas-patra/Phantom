namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class CompanionPairingCompleteRequestDto
{
    public string Code { get; set; } = string.Empty;
    public string CompanionDeviceId { get; set; } = string.Empty;
    public string CompanionDeviceLabel { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
}
