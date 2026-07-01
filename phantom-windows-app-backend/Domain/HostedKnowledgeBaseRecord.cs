namespace Phantom.WindowsApp.Backend.Domain;

public sealed class HostedKnowledgeBaseRecord
{
    public string KnowledgeBaseId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
    public int ChunkCount { get; set; }
    public DateTime? LastProcessedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
