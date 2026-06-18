namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class HostedKnowledgeBaseSearchResultDto
    {
        public HostedKnowledgeBaseSummaryDto KnowledgeBase { get; set; } = new HostedKnowledgeBaseSummaryDto();
        public System.Collections.Generic.IReadOnlyList<HostedKnowledgeBaseSnippetDto> Snippets { get; set; }
            = System.Array.Empty<HostedKnowledgeBaseSnippetDto>();
    }
}
