namespace Phantom.WindowsApp.Backend.Domain;

public sealed class PhoneVerificationChallengeRecord
{
    public string ChallengeId { get; set; } = string.Empty;
    public string PhoneNumberE164 { get; set; } = string.Empty;
    public string PhoneNumberMasked { get; set; } = string.Empty;
    public string DeviceFingerprintHash { get; set; } = string.Empty;
    public string InstallId { get; set; } = string.Empty;
    public string EmailHint { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderSessionId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime? CooldownUntilUtc { get; set; }
    public int SendAttemptCount { get; set; }
    public int VerifyAttemptCount { get; set; }
    public string Status { get; set; } = "pending";
    public string VerificationTokenHash { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
}
