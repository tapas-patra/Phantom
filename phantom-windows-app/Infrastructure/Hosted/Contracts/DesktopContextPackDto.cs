using System;

namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class DesktopContextPackDto
    {
        public string PackId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ResumeText { get; set; } = string.Empty;
        public string JobDescriptionText { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; }
    }
}
