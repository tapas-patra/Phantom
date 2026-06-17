namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class ManagedAiProviderOptionDto
{
    public string ProviderId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public IReadOnlyList<ManagedAiModelOptionDto> Models { get; set; } = Array.Empty<ManagedAiModelOptionDto>();
    public DateTime RefreshedAtUtc { get; set; }
}
