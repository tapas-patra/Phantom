using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Hosted
{
    public interface IHostedAccountClient
    {
        StartupAccountCheckResultDto BuildLocalAccountCheck(AuthSessionDto session);
        StartupAccountCheckResultDto BuildCallbackAccountCheck(AuthCallbackResultDto callbackResult);
    }
}
