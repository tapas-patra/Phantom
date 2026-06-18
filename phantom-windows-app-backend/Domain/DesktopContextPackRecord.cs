namespace Phantom.WindowsApp.Backend.Domain;

public sealed class DesktopContextPackRecord
{
    public string PackId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ResumeText { get; set; } = string.Empty;
    public string JobDescriptionText { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
