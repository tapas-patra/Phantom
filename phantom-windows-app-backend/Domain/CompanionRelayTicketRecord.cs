namespace Phantom.WindowsApp.Backend.Domain;

public sealed class CompanionRelayTicketRecord
{
    public string TicketHash { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string PairingId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
}
