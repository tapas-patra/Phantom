using SecureOverlay.Services;

namespace SecureOverlay.Application.Persistence
{
    public interface IConversationCacheRepository
    {
        ConversationCache? Load();
        void Save(ConversationCache cache);
        void Clear();
    }
}
