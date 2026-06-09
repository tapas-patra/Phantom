namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AdminBalanceWaiverRequestDto
{
    public string UserId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
