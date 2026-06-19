namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AdminPasswordResetStartRequestDto
{
    public string Email { get; set; } = string.Empty;
}
