using System;
using Newtonsoft.Json;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Services;

namespace SecureOverlay.Infrastructure.Persistence
{
    public sealed class SqliteSettingsRepository : ISettingsRepository
    {
        private readonly SqliteRuntimeStore _store;

        public SqliteSettingsRepository(SqliteRuntimeStore store)
        {
            _store = store;
        }

        public AppSettings? Load()
        {
            var payload = _store.ReadPayload(SqliteRuntimeStore.SettingsKey);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<AppSettings>(payload);
        }

        public void Save(AppSettings settings)
        {
            var payload = JsonConvert.SerializeObject(settings, Formatting.Indented);
            _store.UpsertPayload(SqliteRuntimeStore.SettingsKey, payload, DateTime.UtcNow);
        }
    }
}
