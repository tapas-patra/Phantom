using System;

namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class HostedKnowledgeBaseProjectCardDto
    {
        public string ProjectCardId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public bool IsRecent { get; set; }
        public int SortOrder { get; set; }
        public string Role { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public System.Collections.Generic.IReadOnlyList<string> Stack { get; set; }
            = System.Array.Empty<string>();
        public string Architecture { get; set; } = string.Empty;
        public string Challenges { get; set; } = string.Empty;
        public string Impact { get; set; } = string.Empty;
        public System.Collections.Generic.IReadOnlyList<string> SourceDocumentIds { get; set; }
            = System.Array.Empty<string>();
        public DateTime UpdatedAtUtc { get; set; }
    }
}
