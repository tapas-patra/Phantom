namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class FeedbackSubmissionCreateRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool ConsentToPublish { get; set; }
    public string Website { get; set; } = string.Empty;
}

public sealed class FeedbackSubmissionUpdateRequestDto
{
    public string FeedbackId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string AdminNotes { get; set; } = string.Empty;
}
