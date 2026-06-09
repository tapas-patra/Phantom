namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AdminLockClearRequestDto
{
    public string UserId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
