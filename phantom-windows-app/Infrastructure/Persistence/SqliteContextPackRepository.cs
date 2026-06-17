using System;
using Newtonsoft.Json;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Infrastructure.Persistence
{
    public sealed class SqliteContextPackRepository : IContextPackRepository
    {
        private readonly SqliteRuntimeStore _store;

        public SqliteContextPackRepository(SqliteRuntimeStore store)
        {
            _store = store;
        }

        public ContextPackState? Load()
        {
            var payload = _store.ReadPayload(SqliteRuntimeStore.ContextPackStateKey);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<ContextPackState>(payload);
        }

        public void Save(ContextPackState state)
        {
            var payload = JsonConvert.SerializeObject(state, Formatting.Indented);
            _store.UpsertPayload(SqliteRuntimeStore.ContextPackStateKey, payload, DateTime.UtcNow);
        }
    }
}
