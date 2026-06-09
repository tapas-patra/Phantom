namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AuthMagicLinkIssuedDto
{
    public string Email { get; set; } = string.Empty;
    public string MagicLinkUrl { get; set; } = string.Empty;
    public string CallbackUri { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
