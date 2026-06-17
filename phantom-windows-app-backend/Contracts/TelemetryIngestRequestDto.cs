namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class TelemetryIngestRequestDto
{
    public string Category { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public Dictionary<string, string> Attributes { get; set; } = new();
    public DateTime? OccurredAtUtc { get; set; }
}
