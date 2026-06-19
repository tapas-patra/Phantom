namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class ManagedAiCatalogRefreshResultDto
{
    public DateTime RefreshedAtUtc { get; set; }
    public IReadOnlyList<ManagedAiCatalogRefreshProviderResultDto> Providers { get; set; }
        = Array.Empty<ManagedAiCatalogRefreshProviderResultDto>();
}
