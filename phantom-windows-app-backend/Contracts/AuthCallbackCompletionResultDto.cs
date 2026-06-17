namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AuthCallbackCompletionResultDto
{
    public AuthSessionDto Session { get; set; } = new();
    public AuthCallbackResultDto CallbackResult { get; set; } = new();
}
