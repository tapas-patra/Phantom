namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AdminManualLockRequestDto
{
    public string UserId { get; set; } = string.Empty;
    public DateTime? ExpiresAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}
