namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class ManagedAiProviderKeyDto
{
    public string CredentialId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public int Priority { get; set; }
    public DateTime? CooldownUntilUtc { get; set; }
    public string LastFailureCode { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}
