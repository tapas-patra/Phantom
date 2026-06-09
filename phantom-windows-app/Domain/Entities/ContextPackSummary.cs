using System;

namespace SecureOverlay.Domain.Entities
{
    public sealed class ContextPackSummary
    {
        public string PackId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
        public int DocumentCount { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
