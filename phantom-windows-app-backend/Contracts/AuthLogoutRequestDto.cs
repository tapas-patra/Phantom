namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AuthLogoutRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
}
