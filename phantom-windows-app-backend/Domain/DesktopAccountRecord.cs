namespace Phantom.WindowsApp.Backend.Domain;

public sealed class DesktopAccountRecord
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public DateTime? EmailVerifiedAtUtc { get; set; }
    public string AccessTier { get; set; } = "free";
    public string PasswordHash { get; set; } = string.Empty;
    public string PhoneNumberE164 { get; set; } = string.Empty;
    public bool PhoneVerified { get; set; }
    public DateTime? PhoneVerifiedAtUtc { get; set; }
    public string RegistrationDeviceFingerprintHash { get; set; } = string.Empty;
    public DateTime? TermsAcceptedAtUtc { get; set; }
    public string TermsVersion { get; set; } = string.Empty;
    public decimal ProAvailableCredits { get; set; }
    public decimal PremiumAvailableCredits { get; set; }
    public decimal PremiumNegativeCredits { get; set; }
    public DateTime LeaseExpiresAtUtc { get; set; }
    public bool OfflineModeEnabled { get; set; }
    public DateTime LastValidatedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
