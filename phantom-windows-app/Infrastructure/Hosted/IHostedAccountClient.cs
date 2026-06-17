using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Hosted
{
    public interface IHostedAccountClient
    {
        StartupAccountCheckResultDto GetStartupAccountCheck(AuthSessionDto session);
        StartupAccountCheckResultDto GetStartupAccountCheck(AuthCallbackResultDto callbackResult);
        ManagedAiCatalogDto GetManagedCatalog(string accessToken);
    }
}
