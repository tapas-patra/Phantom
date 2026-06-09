using SecureOverlay.Domain.Enums;

namespace SecureOverlay.Application.Auth
{
    public sealed class StartupGateContext
    {
        public StartupGateContext(
            StartupGateState state,
            string title,
            string message,
            bool canOpenMainApp,
            bool canAttemptLogin,
            bool canRegister,
            bool canRetry,
            string? detail = null)
        {
            State = state;
            Title = title;
            Message = message;
            CanOpenMainApp = canOpenMainApp;
            CanAttemptLogin = canAttemptLogin;
            CanRegister = canRegister;
            CanRetry = canRetry;
            Detail = detail;
        }

        public StartupGateState State { get; }
        public string Title { get; }
        public string Message { get; }
        public bool CanOpenMainApp { get; }
        public bool CanAttemptLogin { get; }
        public bool CanRegister { get; }
        public bool CanRetry { get; }
        public string? Detail { get; }
    }
}
