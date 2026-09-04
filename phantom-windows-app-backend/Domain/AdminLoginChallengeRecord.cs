namespace Phantom.WindowsApp.Backend.Domain;

public sealed class AdminLoginChallengeRecord
{
    public string ChallengeId { get; set; } = string.Empty;
    public string AdminId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int FailedAttempts { get; set; }
    public bool Consumed { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
    public string DeliveryError { get; set; } = string.Empty;
}
