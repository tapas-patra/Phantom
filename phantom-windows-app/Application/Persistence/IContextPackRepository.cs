using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Application.Persistence
{
    public interface IContextPackRepository
    {
        ContextPackState? Load();
        void Save(ContextPackState state);
    }
}
