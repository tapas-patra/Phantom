namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class CompanionPairingStartResultDto
{
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public string QrPayload { get; set; } = string.Empty;
}
