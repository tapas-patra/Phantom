using SecureOverlay.Domain.Enums;

namespace SecureOverlay.Domain.Entities
{
    public sealed class AppLaunchContext
    {
        public StartupGateState GateState { get; set; } = StartupGateState.Ready;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public bool CanStartInterview { get; set; } = true;
        public bool CanResumeLockedInterview { get; set; }
        public bool IsRestrictedShell => !CanStartInterview;
    }
}
