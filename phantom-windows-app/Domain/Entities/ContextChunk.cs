namespace SecureOverlay.Domain.Entities
{
    public sealed class ContextChunk
    {
        public string ChunkId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string SearchText { get; set; } = string.Empty;
        public int Order { get; set; }
    }
}
