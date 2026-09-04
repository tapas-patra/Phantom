namespace Phantom.WindowsApp.Backend.Domain;

public sealed class FeedbackSubmissionRecord
{
    public string FeedbackId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool ConsentToPublish { get; set; }
    public string Status { get; set; } = string.Empty;
    public string AdminNotes { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
