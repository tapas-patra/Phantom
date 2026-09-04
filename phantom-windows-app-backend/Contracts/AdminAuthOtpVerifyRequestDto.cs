namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AdminAuthOtpVerifyRequestDto
{
    public string ChallengeId { get; set; } = string.Empty;
    public string OtpCode { get; set; } = string.Empty;
}
