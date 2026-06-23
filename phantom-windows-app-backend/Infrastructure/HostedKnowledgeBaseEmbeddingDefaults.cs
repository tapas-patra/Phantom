namespace Phantom.WindowsApp.Backend.Infrastructure;

public static class HostedKnowledgeBaseEmbeddingDefaults
{
    public const string DefaultProvider = "openai";
    public const string DefaultBaseUrl = "https://api.openai.com/v1";
    public const string DefaultModel = "text-embedding-3-small";
    public const int DefaultDimensions = 1536;
    public const int DefaultVersion = 1;
    public const int DefaultBatchSize = 32;
}
