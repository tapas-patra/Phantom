namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AdminAuthChallengeDto
{
    public bool RequiresOtp { get; set; } = true;
    public string ChallengeId { get; set; } = string.Empty;
    public string MaskedEmail { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
