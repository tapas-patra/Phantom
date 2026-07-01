namespace Phantom.WindowsApp.Backend.Domain;

public sealed class HostedKnowledgeBaseProjectCardRecord
{
    public string ProjectCardId { get; set; } = string.Empty;
    public string KnowledgeBaseId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsRecent { get; set; }
    public int SortOrder { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string StackJson { get; set; } = "[]";
    public string Architecture { get; set; } = string.Empty;
    public string Challenges { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public string SourceDocumentIdsJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
