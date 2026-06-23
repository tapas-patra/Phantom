namespace Phantom.WindowsApp.Backend.Domain;

public sealed class HostedKnowledgeBaseEmbeddingConfigRecord
{
    public string ConfigId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public int Version { get; set; }
    public int BatchSize { get; set; }
    public string EncryptedApiKey { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}
