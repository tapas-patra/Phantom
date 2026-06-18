namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class ManagedAiCatalogRefreshProviderResultDto
{
    public string ProviderId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Attempted { get; set; }
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ModelCount { get; set; }
    public DateTime? RefreshedAtUtc { get; set; }
}
