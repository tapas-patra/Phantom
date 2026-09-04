namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class DeviceSessionRevokeRequestDto
{
    public string DeviceInstallId { get; set; } = string.Empty;
    public string DeviceFingerprintHash { get; set; } = string.Empty;
}
