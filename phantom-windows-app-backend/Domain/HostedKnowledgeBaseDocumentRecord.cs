namespace Phantom.WindowsApp.Backend.Domain;

public sealed class HostedKnowledgeBaseDocumentRecord
{
    public string DocumentId { get; set; } = string.Empty;
    public string KnowledgeBaseId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public int CharacterCount { get; set; }
    public int ChunkCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public DateTime UploadedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
}
