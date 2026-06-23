namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class HostedKnowledgeBaseEmbeddingConfigUpdateRequestDto
{
    public bool IsEnabled { get; set; } = true;
    public string ProviderId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public int Version { get; set; }
    public int BatchSize { get; set; }
    public string ApiKey { get; set; } = string.Empty;
}
