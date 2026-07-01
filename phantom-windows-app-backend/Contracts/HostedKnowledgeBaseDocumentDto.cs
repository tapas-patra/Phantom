namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class HostedKnowledgeBaseDocumentDto
{
    public string DocumentId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = string.Empty;
    public int EmbeddingVersion { get; set; }
    public int CharacterCount { get; set; }
    public int ChunkCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public DateTime UploadedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime? IndexedAtUtc { get; set; }
}
