namespace Phantom.WindowsApp.Backend.Domain;

public sealed class MagicLinkRecord
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string InstallId { get; set; } = string.Empty;
    public string DeviceFingerprintHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool Consumed { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
}
