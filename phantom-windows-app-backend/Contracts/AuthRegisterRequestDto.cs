namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AuthRegisterRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PhoneVerificationToken { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string InstallId { get; set; } = string.Empty;
    public string DeviceLabel { get; set; } = string.Empty;
    public string DeviceFingerprintHash { get; set; } = string.Empty;
    public string SecretFingerprintHint { get; set; } = string.Empty;
    public bool AcceptedTerms { get; set; }
}
