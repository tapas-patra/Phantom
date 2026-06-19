namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class HostedKnowledgeBaseSearchResultDto
{
    public HostedKnowledgeBaseSummaryDto KnowledgeBase { get; set; } = new();
    public IReadOnlyList<HostedKnowledgeBaseSnippetDto> Snippets { get; set; } = Array.Empty<HostedKnowledgeBaseSnippetDto>();
}
