namespace Phantom.WindowsApp.Backend.Domain;

public sealed class KnowledgeBaseEmbeddingProfile
{
    public string ProviderId { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public int Dimensions { get; init; }
    public int Version { get; init; }
}
