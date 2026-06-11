namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class GoogleMailOAuthStatusDto
{
    public bool IsConfigured { get; set; }
    public bool HasRefreshToken { get; set; }
    public string FromEmail { get; set; } = string.Empty;
}
