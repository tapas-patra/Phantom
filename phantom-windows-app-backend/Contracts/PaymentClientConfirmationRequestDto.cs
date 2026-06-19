namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class PaymentClientConfirmationRequestDto
{
    public string CheckoutId { get; set; } = string.Empty;
    public string RazorpayOrderId { get; set; } = string.Empty;
    public string RazorpayPaymentId { get; set; } = string.Empty;
    public string RazorpaySignature { get; set; } = string.Empty;
}
