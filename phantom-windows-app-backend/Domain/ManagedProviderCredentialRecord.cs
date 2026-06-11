namespace Phantom.WindowsApp.Backend.Domain;

public sealed class ManagedProviderCredentialRecord
{
    public string CredentialId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string EncryptedApiKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public int Priority { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
