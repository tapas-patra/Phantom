namespace Phantom.WindowsApp.Backend.Domain;

public sealed class ManagedProviderCatalogRecord
{
    public string ProviderId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ModelsJson { get; set; } = "[]";
    public DateTime RefreshedAtUtc { get; set; }
}
