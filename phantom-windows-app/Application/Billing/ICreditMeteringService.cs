using System;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Application.Billing
{
    public interface ICreditMeteringService
    {
        InterviewSessionActivationResult EnsureInterviewSession();
        InterviewSessionRecord? GetActiveSession();
        TimeSpan GetMeteredElapsed(InterviewSessionRecord session);
        bool PauseActiveSession();
        bool ResumePausedSession();
        InterviewSessionCompletionResult? FinalizeActiveSession();
        void AbandonActiveSession();
    }
}
