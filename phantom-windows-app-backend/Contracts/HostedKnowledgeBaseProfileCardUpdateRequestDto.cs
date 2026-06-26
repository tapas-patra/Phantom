namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class HostedKnowledgeBaseProfileCardUpdateRequestDto
{
    public string FullName { get; set; } = string.Empty;
    public string ResumeText { get; set; } = string.Empty;
    public string ShortIntro { get; set; } = string.Empty;
    public string CurrentRole { get; set; } = string.Empty;
    public int YearsOfExperience { get; set; }
    public IReadOnlyList<string> Strengths { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Skills { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Domains { get; set; } = Array.Empty<string>();
}
