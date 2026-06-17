namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AuthRegisterResultDto
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EmailVerificationRequired { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
    public string DeliveryError { get; set; } = string.Empty;
}
