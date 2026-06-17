namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AuthRefreshRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
    public string InstallId { get; set; } = string.Empty;
    public string DeviceFingerprintHash { get; set; } = string.Empty;
}
