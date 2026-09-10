namespace Phantom.WindowsApp.Backend.Domain;

public sealed class CompanionAuditEventRecord
{
    public string EventId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string PairingId { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
