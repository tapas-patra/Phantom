namespace Phantom.WindowsApp.Backend.Domain;

public sealed class ManagedAiRuntimeSelectionRecord
{
    public string SelectionId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}
