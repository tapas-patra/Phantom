namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class ByoModelCatalogRequestDto
{
    public string ProviderId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}
