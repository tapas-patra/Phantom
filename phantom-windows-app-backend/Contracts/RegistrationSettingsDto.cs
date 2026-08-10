namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class RegistrationSettingsDto
{
    public bool PhoneVerificationRequired { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class RegistrationSettingsUpdateRequestDto
{
    public bool PhoneVerificationRequired { get; set; }
}
