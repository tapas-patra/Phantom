namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class PhoneVerificationStartRequestDto
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string DeviceFingerprintHash { get; set; } = string.Empty;
    public string InstallId { get; set; } = string.Empty;
    public string EmailHint { get; set; } = string.Empty;
}
