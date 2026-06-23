namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class ManagedAiRuntimeSelectionDto
{
    public bool IsConfigured { get; set; }
    public bool IsResolved { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderLabel { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string ModelDisplayName { get; set; } = string.Empty;
    public DateTime? UpdatedAtUtc { get; set; }
}
