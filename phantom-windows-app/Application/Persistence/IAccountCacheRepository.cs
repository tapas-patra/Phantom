using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Application.Persistence
{
    public interface IAccountCacheRepository
    {
        AccountCacheSnapshot? Load();
        void Save(AccountCacheSnapshot snapshot);
        void Clear();
    }
}
