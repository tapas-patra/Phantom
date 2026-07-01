namespace SecureOverlay.Domain.Entities
{
    public sealed class RetrievedContextSnippet
    {
        public string DocumentTitle { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public int Score { get; set; }
    }
}
