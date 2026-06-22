namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class UserPasswordResetStartRequestDto
{
    public string Email { get; set; } = string.Empty;
}
