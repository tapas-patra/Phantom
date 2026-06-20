namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AdminAccountUpdateRequestDto
{
    public string UserId { get; set; } = string.Empty;
    public string AccessTier { get; set; } = "free";
    public decimal ProAvailableCredits { get; set; }
    public decimal PremiumAvailableCredits { get; set; }
    public decimal PremiumNegativeCredits { get; set; }
    public bool OfflineModeEnabled { get; set; }
    public string Reason { get; set; } = string.Empty;
}
