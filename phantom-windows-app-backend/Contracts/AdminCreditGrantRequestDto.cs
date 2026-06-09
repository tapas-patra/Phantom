namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AdminCreditGrantRequestDto
{
    public string UserId { get; set; } = string.Empty;
    public decimal ProCreditsToAdd { get; set; }
    public decimal PremiumCreditsToAdd { get; set; }
    public string Reason { get; set; } = string.Empty;
}
