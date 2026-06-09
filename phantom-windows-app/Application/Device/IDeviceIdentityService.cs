using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Application.Device
{
    public interface IDeviceIdentityService
    {
        DeviceProfile GetOrCreateProfile();
    }
}
