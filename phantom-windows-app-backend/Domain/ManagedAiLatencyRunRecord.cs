namespace Phantom.WindowsApp.Backend.Domain;

public sealed class ManagedAiLatencyRunRecord
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TotalModels { get; set; }
    public int ProcessedModels { get; set; }
    public string Error { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
