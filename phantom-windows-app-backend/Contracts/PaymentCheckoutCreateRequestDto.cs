namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class PaymentCheckoutCreateRequestDto
{
    public string Target { get; set; } = string.Empty;
    public string PackCode { get; set; } = string.Empty;
}
