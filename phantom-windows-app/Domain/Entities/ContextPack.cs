using System;

namespace SecureOverlay.Domain.Entities
{
    public sealed class ContextPack
    {
        public string PackId { get; set; } = "default";
        public string Name { get; set; } = "Default Context Pack";
        public string ResumeText { get; set; } = string.Empty;
        public string ResumeSummary { get; set; } = string.Empty;
        public string JobDescriptionText { get; set; } = string.Empty;
        public string JobDescriptionSummary { get; set; } = string.Empty;
        public System.Collections.Generic.List<ContextDocument> Documents { get; set; } = new System.Collections.Generic.List<ContextDocument>();
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
