namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AuthCallbackResultDto
{
    public string Email { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool PhoneVerified { get; set; }
    public string DeviceInstallId { get; set; } = string.Empty;
    public string DeviceFingerprintHash { get; set; } = string.Empty;
}
