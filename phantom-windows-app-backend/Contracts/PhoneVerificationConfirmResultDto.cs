namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class PhoneVerificationConfirmResultDto
{
    public string VerificationToken { get; set; } = string.Empty;
    public string MaskedPhoneNumber { get; set; } = string.Empty;
    public DateTime VerifiedAtUtc { get; set; }
}
