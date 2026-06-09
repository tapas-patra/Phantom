namespace SecureOverlay.Application.Auth
{
    public interface IStartupGateService
    {
        StartupGateContext GetInitialContext();
        StartupGateContext BeginLogin();
        StartupGateContext CompleteLogin(string email, bool useMagicLink);
        StartupGateContext CompleteLogin(string email, string password, bool useMagicLink);
        StartupGateContext ProcessAuthCallback(string callbackUri);
        StartupGateContext ResetToAuthChoice();
        StartupGateContext Retry();
    }
}
