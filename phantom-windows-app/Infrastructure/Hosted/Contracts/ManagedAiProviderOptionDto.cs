using System;
using System.Collections.Generic;

namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class ManagedAiProviderOptionDto
    {
        public string ProviderId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public List<ManagedAiModelOptionDto> Models { get; set; } = new List<ManagedAiModelOptionDto>();
        public DateTime RefreshedAtUtc { get; set; }
    }
}
