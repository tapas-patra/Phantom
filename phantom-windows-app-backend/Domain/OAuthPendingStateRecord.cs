namespace Phantom.WindowsApp.Backend.Domain;

public sealed class OAuthPendingStateRecord
{
    public string Provider { get; set; } = string.Empty;
    public string StateToken { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
