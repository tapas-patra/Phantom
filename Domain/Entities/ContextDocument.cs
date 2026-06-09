using System;
using System.Collections.Generic;

namespace SecureOverlay.Domain.Entities
{
    public sealed class ContextDocument
    {
        public string DocumentId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; }
        public List<ContextChunk> Chunks { get; set; } = new List<ContextChunk>();
    }
}
