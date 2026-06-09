using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Hosted
{
    public interface IHostedAuthClient
    {
        AuthSessionDto CreateSession(AuthLoginRequestDto request);
        AuthCallbackCompletionResultDto CompleteCallback(AuthCallbackCompletionRequestDto request);
    }
}
