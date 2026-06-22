namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class UserPasswordResetCompleteRequestDto
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
