using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Application.Persistence
{
    public interface IInterviewSessionRepository
    {
        InterviewSessionRecord? Load();
        void Save(InterviewSessionRecord session);
        void Clear();
    }
}
