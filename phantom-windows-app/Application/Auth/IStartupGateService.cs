using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Application.Auth
{
    public interface IStartupGateService
    {
        StartupGateContext GetInitialContext();
        StartupGateContext BeginLogin();
        StartupGateContext CompleteLogin(string email, string password);
        StartupGateContext ResetToAuthChoice();
        StartupGateContext Retry();
    }
}
