using System;

namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class HostedKnowledgeBaseProfileCardDto
    {
        public string ProfileCardId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string ResumeText { get; set; } = string.Empty;
        public string CandidateInfo { get; set; } = string.Empty;
        public string ShortIntro { get; set; } = string.Empty;
        public string CurrentRole { get; set; } = string.Empty;
        public int YearsOfExperience { get; set; }
        public System.Collections.Generic.IReadOnlyList<string> Strengths { get; set; }
            = System.Array.Empty<string>();
        public System.Collections.Generic.IReadOnlyList<string> Skills { get; set; }
            = System.Array.Empty<string>();
        public System.Collections.Generic.IReadOnlyList<string> Domains { get; set; }
            = System.Array.Empty<string>();
        public System.Collections.Generic.IReadOnlyList<string> SourceDocumentIds { get; set; }
            = System.Array.Empty<string>();
        public DateTime UpdatedAtUtc { get; set; }
    }
}
