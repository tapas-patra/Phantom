using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Infrastructure.Persistence
{
    public sealed class SqliteUsageReconciliationRepository : IUsageReconciliationRepository
    {
        private readonly SqliteRuntimeStore _store;

        public SqliteUsageReconciliationRepository(SqliteRuntimeStore store)
        {
            _store = store;
        }

        public List<UsageReconciliationRecord> LoadAll()
        {
            var payload = _store.ReadPayload(SqliteRuntimeStore.UsageReconciliationQueueKey);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new List<UsageReconciliationRecord>();
            }

            return JsonConvert.DeserializeObject<List<UsageReconciliationRecord>>(payload)
                   ?? new List<UsageReconciliationRecord>();
        }

        public void SaveAll(List<UsageReconciliationRecord> records)
        {
            var payload = JsonConvert.SerializeObject(records ?? new List<UsageReconciliationRecord>(), Formatting.Indented);
            _store.UpsertPayload(SqliteRuntimeStore.UsageReconciliationQueueKey, payload, DateTime.UtcNow);
        }
    }
}
