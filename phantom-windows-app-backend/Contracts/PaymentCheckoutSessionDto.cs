namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class PaymentCheckoutSessionDto
{
    public string CheckoutId { get; set; } = string.Empty;
    public string RazorpayOrderId { get; set; } = string.Empty;
    public string RazorpayKeyId { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string PackCode { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public int AmountMinor { get; set; }
    public string Currency { get; set; } = "INR";
    public decimal Credits { get; set; }
    public decimal PremiumDebtCreditsCovered { get; set; }
    public string Status { get; set; } = "created";
}
