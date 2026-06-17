namespace Phantom.WindowsApp.Backend.Domain;

public sealed class DesktopSessionRecord
{
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AccessTokenHash { get; set; } = string.Empty;
    public string RefreshTokenHash { get; set; } = string.Empty;
    public string AuthMethod { get; set; } = string.Empty;
    public string DeviceInstallId { get; set; } = string.Empty;
    public string DeviceFingerprintHash { get; set; } = string.Empty;
    public DateTime AuthenticatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsAuthenticated { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
