namespace Phantom.WindowsApp.Backend.Domain;

public sealed class UsageLedgerRecord
{
    public string LedgerEntryId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
    public decimal ChargedCredits { get; set; }
    public int ChargedBlocks { get; set; }
    public decimal ChargedProCredits { get; set; }
    public decimal ChargedPremiumCredits { get; set; }
    public decimal AddedPremiumDebt { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
