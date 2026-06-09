using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Hosted
{
    public interface IHostedAuthClient
    {
        AuthSessionDto CreateLocalSession(string email, bool useMagicLink);
        AuthCallbackResultDto ParseCallback(string callbackUri);
    }
}
