namespace Phantom.WindowsApp.Backend.Domain;

public sealed class HostedKnowledgeBaseProfileCardRecord
{
    public string ProfileCardId { get; set; } = string.Empty;
    public string KnowledgeBaseId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string ResumeText { get; set; } = string.Empty;
    public string ShortIntro { get; set; } = string.Empty;
    public string CurrentRole { get; set; } = string.Empty;
    public int YearsOfExperience { get; set; }
    public string StrengthsJson { get; set; } = "[]";
    public string SkillsJson { get; set; } = "[]";
    public string DomainsJson { get; set; } = "[]";
    public string SourceDocumentIdsJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
