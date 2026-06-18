namespace Phantom.WindowsApp.Backend.Domain;

public sealed class PaymentWebhookEventRecord
{
    public string EventRecordId { get; set; } = string.Empty;
    public string ExternalEventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
}
