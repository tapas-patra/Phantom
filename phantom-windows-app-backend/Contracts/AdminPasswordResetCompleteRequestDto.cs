namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AdminPasswordResetCompleteRequestDto
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
