namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AuthEmailVerificationResultDto
{
    public string Email { get; set; } = string.Empty;
    public bool Verified { get; set; }
    public string Message { get; set; } = string.Empty;
}
