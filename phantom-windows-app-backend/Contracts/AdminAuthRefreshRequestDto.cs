namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AdminAuthRefreshRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
}
