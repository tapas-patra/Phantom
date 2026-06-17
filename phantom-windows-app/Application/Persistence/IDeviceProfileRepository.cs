using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Application.Persistence
{
    public interface IDeviceProfileRepository
    {
        DeviceProfile? Load();
        void Save(DeviceProfile profile);
    }
}
