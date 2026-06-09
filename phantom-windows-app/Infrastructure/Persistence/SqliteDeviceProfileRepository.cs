using System;
using Newtonsoft.Json;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Infrastructure.Persistence
{
    public sealed class SqliteDeviceProfileRepository : IDeviceProfileRepository
    {
        private readonly SqliteRuntimeStore _store;

        public SqliteDeviceProfileRepository(SqliteRuntimeStore store)
        {
            _store = store;
        }

        public DeviceProfile? Load()
        {
            var payload = _store.ReadPayload(SqliteRuntimeStore.DeviceProfileKey);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<DeviceProfile>(payload);
        }

        public void Save(DeviceProfile profile)
        {
            var payload = JsonConvert.SerializeObject(profile, Formatting.Indented);
            _store.UpsertPayload(SqliteRuntimeStore.DeviceProfileKey, payload, DateTime.UtcNow);
        }
    }
}
