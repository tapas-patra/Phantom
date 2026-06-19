namespace Phantom.WindowsApp.Backend.Domain;

public sealed class AdminPasswordResetTokenRecord
{
    public string TokenHash { get; set; } = string.Empty;
    public string AdminId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool Consumed { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
    public string DeliveryError { get; set; } = string.Empty;
}
