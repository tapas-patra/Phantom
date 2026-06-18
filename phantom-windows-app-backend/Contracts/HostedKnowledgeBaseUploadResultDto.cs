namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class HostedKnowledgeBaseUploadResultDto
{
    public HostedKnowledgeBaseSummaryDto KnowledgeBase { get; set; } = new();
    public IReadOnlyList<HostedKnowledgeBaseDocumentDto> AddedDocuments { get; set; } = Array.Empty<HostedKnowledgeBaseDocumentDto>();
}
