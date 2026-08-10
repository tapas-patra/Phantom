namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class HostedKnowledgeBaseExperienceCardUpdateRequestDto
{
    public string Company { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public int SortOrder { get; set; }
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Responsibilities { get; set; } = string.Empty;
    public IReadOnlyList<string> Skills { get; set; } = Array.Empty<string>();
}
