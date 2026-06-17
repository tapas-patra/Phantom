using System;
using System.Collections.Generic;

namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class ManagedAiCatalogDto
    {
        public List<ManagedAiProviderOptionDto> Providers { get; set; } = new List<ManagedAiProviderOptionDto>();
        public DateTime RefreshedAtUtc { get; set; }
    }
}
