namespace Phantom.WindowsApp.Backend.Domain;

public sealed class PaymentOrderRecord
{
    public string CheckoutId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string PackCode { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string Currency { get; set; } = "INR";
    public int AmountMinor { get; set; }
    public decimal Credits { get; set; }
    public decimal PremiumDebtCreditsCovered { get; set; }
    public string RazorpayOrderId { get; set; } = string.Empty;
    public string RazorpayPaymentId { get; set; } = string.Empty;
    public string RazorpaySignature { get; set; } = string.Empty;
    public string Status { get; set; } = "created";
    public bool ClientConfirmed { get; set; }
    public DateTime? CreditedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
