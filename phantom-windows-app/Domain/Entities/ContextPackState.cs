using System.Collections.Generic;

namespace SecureOverlay.Domain.Entities
{
    public sealed class ContextPackState
    {
        public string SelectedPackId { get; set; } = "default";
        public List<ContextPack> Packs { get; set; } = new List<ContextPack>();
    }
}
