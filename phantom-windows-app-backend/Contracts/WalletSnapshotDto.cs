namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class WalletSnapshotDto
{
    public decimal ProAvailableCredits { get; set; }
    public decimal PremiumAvailableCredits { get; set; }
    public decimal PremiumNegativeCredits { get; set; }
}
