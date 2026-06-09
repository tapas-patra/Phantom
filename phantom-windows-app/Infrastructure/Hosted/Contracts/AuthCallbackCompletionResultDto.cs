namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class AuthCallbackCompletionResultDto
    {
        public AuthSessionDto Session { get; set; } = new AuthSessionDto();
        public AuthCallbackResultDto CallbackResult { get; set; } = new AuthCallbackResultDto();
    }
}
