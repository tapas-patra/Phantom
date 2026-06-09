using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Hosted
{
    public sealed class HttpHostedAccountClient : HttpHostedClientBase, IHostedAccountClient
    {
        public HttpHostedAccountClient(HostedRuntimeOptions options)
            : base(options)
        {
        }

        public StartupAccountCheckResultDto GetStartupAccountCheck(AuthSessionDto session)
        {
            return PostJson<AuthSessionDto, StartupAccountCheckResultDto>(
                "/api/desktop/account/startup-check/session",
                session);
        }

        public StartupAccountCheckResultDto GetStartupAccountCheck(AuthCallbackResultDto callbackResult)
        {
            return PostJson<AuthCallbackResultDto, StartupAccountCheckResultDto>(
                "/api/desktop/account/startup-check/callback",
                callbackResult);
        }
    }
}
