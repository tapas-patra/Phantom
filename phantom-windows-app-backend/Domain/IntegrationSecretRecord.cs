namespace Phantom.WindowsApp.Backend.Domain;

public sealed class IntegrationSecretRecord
{
    public string SecretKey { get; set; } = string.Empty;
    public string EncryptedValue { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}
