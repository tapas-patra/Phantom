using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Application.Persistence
{
    public interface IAuthSessionRepository
    {
        AuthSessionCache? Load();
        void Save(AuthSessionCache session);
        void Clear();
    }
}
