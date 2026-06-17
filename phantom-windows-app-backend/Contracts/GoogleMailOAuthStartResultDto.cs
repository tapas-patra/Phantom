namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class GoogleMailOAuthStartResultDto
{
    public string AuthorizationUrl { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
}
