namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class PhoneVerificationStartResultDto
{
    public string ChallengeId { get; set; } = string.Empty;
    public string MaskedPhoneNumber { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public int RetryAfterSeconds { get; set; }
    public string Status { get; set; } = "pending";
}
