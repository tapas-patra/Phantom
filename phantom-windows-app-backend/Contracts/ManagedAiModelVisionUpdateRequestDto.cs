namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class ManagedAiModelVisionUpdateRequestDto
{
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public bool SupportsVision { get; set; }
}
