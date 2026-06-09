namespace Phantom.WindowsApp.Backend.Domain;

public sealed class TelemetryEventRecord
{
    public string EventId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
