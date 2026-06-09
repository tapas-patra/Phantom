namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class DeviceLockReleaseRequestDto
{
    public string SessionId { get; set; } = string.Empty;
    public string LockToken { get; set; } = string.Empty;
    public string ReleaseReason { get; set; } = string.Empty;
}
