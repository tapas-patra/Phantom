using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Infrastructure.Persistence
{
    public sealed class SqliteTelemetryRepository : ITelemetryRepository
    {
        private readonly SqliteRuntimeStore _store;

        public SqliteTelemetryRepository(SqliteRuntimeStore store)
        {
            _store = store;
        }

        public List<TelemetryEvent> LoadAll()
        {
            var payload = _store.ReadPayload(SqliteRuntimeStore.TelemetryQueueKey);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new List<TelemetryEvent>();
            }

            return JsonConvert.DeserializeObject<List<TelemetryEvent>>(payload) ?? new List<TelemetryEvent>();
        }

        public void SaveAll(List<TelemetryEvent> events)
        {
            var payload = JsonConvert.SerializeObject(events ?? new List<TelemetryEvent>(), Formatting.Indented);
            _store.UpsertPayload(SqliteRuntimeStore.TelemetryQueueKey, payload, DateTime.UtcNow);
        }
    }
}
