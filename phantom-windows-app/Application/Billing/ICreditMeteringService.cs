using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Application.Billing
{
    public interface ICreditMeteringService
    {
        InterviewSessionActivationResult EnsureInterviewSession();
        InterviewSessionRecord? GetActiveSession();
        InterviewSessionCompletionResult? FinalizeActiveSession();
        void AbandonActiveSession();
    }
}
