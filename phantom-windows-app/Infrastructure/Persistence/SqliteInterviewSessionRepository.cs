using System;
using Newtonsoft.Json;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Infrastructure.Persistence
{
    public sealed class SqliteInterviewSessionRepository : IInterviewSessionRepository
    {
        private readonly SqliteRuntimeStore _store;

        public SqliteInterviewSessionRepository(SqliteRuntimeStore store)
        {
            _store = store;
        }

        public InterviewSessionRecord? Load()
        {
            var payload = _store.ReadPayload(SqliteRuntimeStore.InterviewSessionKey);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<InterviewSessionRecord>(payload);
        }

        public void Save(InterviewSessionRecord session)
        {
            var payload = JsonConvert.SerializeObject(session, Formatting.Indented);
            _store.UpsertPayload(SqliteRuntimeStore.InterviewSessionKey, payload, DateTime.UtcNow);
        }

        public void Clear()
        {
            _store.DeletePayload(SqliteRuntimeStore.InterviewSessionKey);
        }
    }
}
