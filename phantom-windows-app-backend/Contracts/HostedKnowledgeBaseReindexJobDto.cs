namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class HostedKnowledgeBaseReindexJobDto
{
    public string JobId { get; set; } = string.Empty;
    public string KnowledgeBaseId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public string TargetEmbeddingModel { get; set; } = string.Empty;
    public int TargetEmbeddingVersion { get; set; }
    public int TotalDocuments { get; set; }
    public int ProcessedDocuments { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
