using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Application.Interviews
{
    public interface IInterviewLockService
    {
        InterviewLockHeartbeatResult StartOrResumeLock(InterviewSessionRecord session);
        InterviewLockHeartbeatResult HeartbeatActiveLock();
        void MarkLockReleased();
    }
}
