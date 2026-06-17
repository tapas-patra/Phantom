namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class ManagedAiProviderKeyUpsertRequestDto
{
    public string CredentialId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; }
}
