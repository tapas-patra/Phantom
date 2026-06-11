namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class ManagedAiCatalogDto
{
    public IReadOnlyList<ManagedAiProviderOptionDto> Providers { get; set; } = Array.Empty<ManagedAiProviderOptionDto>();
}
