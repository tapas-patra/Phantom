namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AdminAuthSessionDto
{
    public string AdminId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string AuthMethod { get; set; } = string.Empty;
    public DateTime AuthenticatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsAuthenticated { get; set; }
}
