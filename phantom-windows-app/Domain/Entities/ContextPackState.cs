using System.Collections.Generic;

namespace SecureOverlay.Domain.Entities
{
    public sealed class ContextPackState
    {
        public string SelectedPackId { get; set; } = "default";
        public ContextPack LocalDraftPack { get; set; } = new ContextPack
        {
            PackId = "default",
            Name = "Local Context Pack"
        };
        public List<ContextPack> Packs { get; set; } = new List<ContextPack>();
    }
}
