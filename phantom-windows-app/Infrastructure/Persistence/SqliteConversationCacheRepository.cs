using System;
using Newtonsoft.Json;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Services;

namespace SecureOverlay.Infrastructure.Persistence
{
    public sealed class SqliteConversationCacheRepository : IConversationCacheRepository
    {
        private readonly SqliteRuntimeStore _store;

        public SqliteConversationCacheRepository(SqliteRuntimeStore store)
        {
            _store = store;
        }

        public ConversationCache? Load()
        {
            var payload = _store.ReadPayload(SqliteRuntimeStore.ConversationCacheKey);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<ConversationCache>(payload);
        }

        public void Save(ConversationCache cache)
        {
            var payload = JsonConvert.SerializeObject(cache, Formatting.Indented);
            _store.UpsertPayload(SqliteRuntimeStore.ConversationCacheKey, payload, DateTime.UtcNow);
        }

        public void Clear()
        {
            _store.DeletePayload(SqliteRuntimeStore.ConversationCacheKey);
        }
    }
}
