namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AdminAccountSnapshotDto
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool PhoneVerified { get; set; }
    public decimal ProAvailableCredits { get; set; }
    public decimal PremiumAvailableCredits { get; set; }
    public decimal PremiumNegativeCredits { get; set; }
    public DateTime LeaseExpiresAtUtc { get; set; }
    public bool OfflineModeEnabled { get; set; }
    public DateTime LastValidatedAtUtc { get; set; }
    public string ActiveLockSessionId { get; set; } = string.Empty;
    public string ActiveLockDeviceId { get; set; } = string.Empty;
    public DateTime? ActiveLockExpiresAtUtc { get; set; }
    public List<UsageLedgerSummaryDto> RecentLedgerEntries { get; set; } = new();
}
