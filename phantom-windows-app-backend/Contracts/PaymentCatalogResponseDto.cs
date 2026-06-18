namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class PaymentCatalogResponseDto
{
    public string RazorpayKeyId { get; set; } = string.Empty;
    public PaymentPackDto[] ProPacks { get; set; } = [];
    public PaymentPackDto[] PremiumPacks { get; set; } = [];
    public object? PremiumDebtSettlement { get; set; }
}
