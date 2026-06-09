using System;
using Newtonsoft.Json;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Infrastructure.Persistence
{
    public sealed class SqliteAccountCacheRepository : IAccountCacheRepository
    {
        private readonly SqliteRuntimeStore _store;

        public SqliteAccountCacheRepository(SqliteRuntimeStore store)
        {
            _store = store;
        }

        public AccountCacheSnapshot? Load()
        {
            var payload = _store.ReadPayload(SqliteRuntimeStore.AccountCacheKey);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<AccountCacheSnapshot>(payload);
        }

        public void Save(AccountCacheSnapshot snapshot)
        {
            var payload = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
            _store.UpsertPayload(SqliteRuntimeStore.AccountCacheKey, payload, DateTime.UtcNow);
        }

        public void Clear()
        {
            _store.DeletePayload(SqliteRuntimeStore.AccountCacheKey);
        }
    }
}
