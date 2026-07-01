namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class HostedKnowledgeBaseEmbeddingConfigDto
{
    public bool IsEnabled { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public int Version { get; set; }
    public int BatchSize { get; set; }
    public bool HasApiKey { get; set; }
    public bool IsConfigured { get; set; }
    public string ConfigSource { get; set; } = string.Empty;
    public DateTime? UpdatedAtUtc { get; set; }
}
