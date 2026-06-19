namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class PhoneVerificationConfirmRequestDto
{
    public string ChallengeId { get; set; } = string.Empty;
    public string OtpCode { get; set; } = string.Empty;
}
