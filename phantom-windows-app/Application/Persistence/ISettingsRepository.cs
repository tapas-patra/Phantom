using SecureOverlay.Services;

namespace SecureOverlay.Application.Persistence
{
    public interface ISettingsRepository
    {
        AppSettings? Load();
        void Save(AppSettings settings);
    }
}
