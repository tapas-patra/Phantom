namespace Phantom.WindowsApp.Backend.Domain;

public sealed class HostedKnowledgeBaseExperienceCardRecord
{
    public string ExperienceCardId { get; set; } = string.Empty;
    public string KnowledgeBaseId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public int SortOrder { get; set; }
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Responsibilities { get; set; } = string.Empty;
    public string SkillsJson { get; set; } = "[]";
    public string SourceDocumentIdsJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
