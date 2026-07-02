namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class ManagedAiLatencyModelStatusDto
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderLabel { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool SupportsVision { get; set; }
    public bool? IsChatCapable { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? LatencyMs { get; set; }
    public DateTime? CheckedAtUtc { get; set; }
    public string LastJobId { get; set; } = string.Empty;
}
