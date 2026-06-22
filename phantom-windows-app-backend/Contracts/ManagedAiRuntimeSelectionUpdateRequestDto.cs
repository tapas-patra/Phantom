namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class ManagedAiRuntimeSelectionUpdateRequestDto
{
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
}
