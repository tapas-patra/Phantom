namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AuthCallbackCompletionRequestDto
{
    public string CallbackUri { get; set; } = string.Empty;
    public string InstallId { get; set; } = string.Empty;
    public string DeviceFingerprintHash { get; set; } = string.Empty;
}
