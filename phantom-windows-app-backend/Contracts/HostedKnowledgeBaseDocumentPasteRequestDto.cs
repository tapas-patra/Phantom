namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class HostedKnowledgeBaseDocumentPasteRequestDto
{
    public string Section { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
