namespace Phantom.WindowsApp.Backend.Domain;

public sealed class ManagedAiLatencyModelStatusRecord
{
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string ModelDisplayName { get; set; } = string.Empty;
    public bool SupportsVision { get; set; }
    public bool? IsChatCapable { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? LatencyMs { get; set; }
    public DateTime? CheckedAtUtc { get; set; }
    public string LastJobId { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}
