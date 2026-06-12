using System;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Application.Billing
{
    public interface ICreditMeteringService
    {
        InterviewSessionActivationResult EnsureInterviewSession();
        InterviewSessionRecord? GetActiveSession();
        TimeSpan GetMeteredElapsed(InterviewSessionRecord session);
        bool TrackUsageSource(SecureOverlay.Domain.Enums.InterviewUsageSource source, string providerId);
        bool PauseActiveSession();
        bool ResumePausedSession();
        InterviewSessionCompletionResult? FinalizeActiveSession();
        void AbandonActiveSession();
    }
}
