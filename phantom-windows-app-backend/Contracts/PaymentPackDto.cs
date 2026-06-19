namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class PaymentPackDto
{
    public string Target { get; set; } = string.Empty;
    public string PackCode { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal Credits { get; set; }
    public decimal DisplayAmountInr { get; set; }
    public int AmountMinor { get; set; }
    public string Description { get; set; } = string.Empty;
}
