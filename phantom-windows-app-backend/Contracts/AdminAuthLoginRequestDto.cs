namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AdminAuthLoginRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
