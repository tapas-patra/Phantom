namespace Phantom.WindowsApp.Backend.Domain;

public sealed class HostedKnowledgeBaseChunkRecord
{
    public string ChunkId { get; set; } = string.Empty;
    public string KnowledgeBaseId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string SearchText { get; set; } = string.Empty;
    public string EmbeddingJson { get; set; } = "[]";
    public int TokenCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
