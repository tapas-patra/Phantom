namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class UsageLedgerSummaryDto
{
    public string LedgerEntryId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public decimal ChargedCredits { get; set; }
    public int ChargedBlocks { get; set; }
    public decimal AddedPremiumDebt { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
