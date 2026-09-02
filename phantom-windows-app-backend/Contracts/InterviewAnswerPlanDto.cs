namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class InterviewAnswerPlanDto
{
    public string Intent { get; set; } = "ambiguous";
    public string Source { get; set; } = "Clarification";
    public string EntityType { get; set; } = "none";
    public string EntityId { get; set; } = string.Empty;
    public bool Retrieve { get; set; }
    public string AnswerMode { get; set; } = "clarification";
    public IReadOnlyList<string> AnswerOutline { get; set; } = Array.Empty<string>();
    public bool AllowCode { get; set; }
    public double Confidence { get; set; }
    public string RetrievalQuery { get; set; } = string.Empty;
    public IReadOnlyList<string> PreferredDocumentIds { get; set; } = Array.Empty<string>();
}
