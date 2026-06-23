namespace Phantom.WindowsApp.Backend.Domain;

public sealed class HostedKnowledgeBaseSearchCandidateRecord
{
    public string ChunkId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string DocumentTitle { get; set; } = string.Empty;
    public string SectionTitle { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string SearchText { get; set; } = string.Empty;
    public double LexicalScore { get; set; }
    public double SemanticSimilarity { get; set; }
    public double FusedScore { get; set; }
}
