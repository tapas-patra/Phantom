using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Application.Auth
{
    public interface IStartupGateService
    {
        StartupGateContext GetInitialContext();
        StartupGateContext BeginLogin();
        StartupGateContext CompleteLogin(string email, bool useMagicLink);
        StartupGateContext CompleteLogin(string email, string password, bool useMagicLink);
        AuthMagicLinkIssuedDto RequestMagicLink(string email);
        StartupGateContext ProcessAuthCallback(string callbackUri);
        StartupGateContext ResetToAuthChoice();
        StartupGateContext Retry();
    }
}
