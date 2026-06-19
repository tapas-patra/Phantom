namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class GoogleMailOAuthStatusDto
{
    public bool IsConfigured { get; set; }
    public bool HasRefreshToken { get; set; }
    public bool HasValidRefreshToken { get; set; }
    public bool NeedsReconnect { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
}
