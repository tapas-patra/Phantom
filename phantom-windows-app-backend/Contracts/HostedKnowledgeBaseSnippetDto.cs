namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class HostedKnowledgeBaseSnippetDto
{
    public string DocumentId { get; set; } = string.Empty;
    public string DocumentTitle { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public double Score { get; set; }
}
