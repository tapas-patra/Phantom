using System;
using System.Security.Cryptography;
using System.Text;
using SecureOverlay.Application.Billing;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Domain.Enums;

namespace SecureOverlay.Infrastructure.Billing
{
    public sealed class LocalCreditMeteringService : ICreditMeteringService
    {
        private const decimal CreditsPerBlock = 0.25m;
        private const decimal ProtectedContinuationCap = 1.0m;
        private static readonly TimeSpan MeteringBlock = TimeSpan.FromMinutes(15);

        private readonly IAuthSessionRepository _authSessionRepository;
        private readonly IAccountCacheRepository _accountCacheRepository;
        private readonly IInterviewSessionRepository _interviewSessionRepository;

        public LocalCreditMeteringService(
            IAuthSessionRepository authSessionRepository,
            IAccountCacheRepository accountCacheRepository,
            IInterviewSessionRepository interviewSessionRepository)
        {
            _authSessionRepository = authSessionRepository;
            _accountCacheRepository = accountCacheRepository;
            _interviewSessionRepository = interviewSessionRepository;
        }

        public InterviewSessionActivationResult EnsureInterviewSession()
        {
            var existingSession = _interviewSessionRepository.Load();
            if (existingSession != null && existingSession.State == InterviewSessionState.Active)
            {
                return new InterviewSessionActivationResult
                {
                    Allowed = true,
                    StartedNewSession = false,
                    ResumedExistingSession = true,
                    Title = "Interview Resumed",
                    Message = "Resuming the current locked interview session on this device.",
                    Session = existingSession
                };
            }

            var authSession = _authSessionRepository.Load();
            if (authSession == null || !authSession.IsAuthenticated)
            {
                return Denied("Login Required", "A signed-in account is required before an interview can start.");
            }

            var snapshot = _accountCacheRepository.Load();
            if (snapshot == null)
            {
                return Denied("Account Check Required", "No cached account snapshot is available for interview metering.");
            }

            if (!snapshot.EmailVerified)
            {
                return Denied("Email Verification Required", "Email verification must complete before the app can start an interview.");
            }

            if (snapshot.PremiumNegativeCredits > 0m)
            {
                return Denied("Negative Premium Balance", "New interviews stay blocked until the Premium debt is cleared.");
            }

            var ledger = ResolveEligibleLedger(snapshot);
            if (!ledger.HasValue)
            {
                return Denied("No Credits Available", "At least one full 15 minute block must be available before a new interview starts.");
            }

            var session = new InterviewSessionRecord
            {
                SessionId = $"session-{Guid.NewGuid():N}",
                UserId = authSession.UserId,
                StartedAtUtc = DateTime.UtcNow,
                LastHeartbeatAtUtc = DateTime.UtcNow,
                LockExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
                State = InterviewSessionState.Active,
                PrimaryLedger = ledger.Value,
                LockTokenHash = HashToken(Guid.NewGuid().ToString("N")),
                HeartbeatIntervalSeconds = 60,
                LockTtlSeconds = 300
            };

            snapshot.HasResumableLockedSession = true;
            snapshot.LastLockedSessionId = session.SessionId;
            snapshot.LastLockTokenHash = session.LockTokenHash;
            snapshot.LastValidatedAtUtc = DateTime.UtcNow;

            _interviewSessionRepository.Save(session);
            _accountCacheRepository.Save(snapshot);

            return new InterviewSessionActivationResult
            {
                Allowed = true,
                StartedNewSession = true,
                ResumedExistingSession = false,
                Title = "Interview Started",
                Message = $"Metering started on the {session.PrimaryLedger} balance.",
                Session = session
            };
        }

        public InterviewSessionRecord? GetActiveSession()
        {
            var session = _interviewSessionRepository.Load();
            if (session == null || session.State != InterviewSessionState.Active)
            {
                return null;
            }

            return session;
        }

        public InterviewSessionCompletionResult? FinalizeActiveSession()
        {
            var session = _interviewSessionRepository.Load();
            var snapshot = _accountCacheRepository.Load();
            if (session == null || snapshot == null || session.State != InterviewSessionState.Active)
            {
                return null;
            }

            var endedAtUtc = DateTime.UtcNow;
            var duration = endedAtUtc - session.StartedAtUtc;
            var blocks = Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes / MeteringBlock.TotalMinutes));
            var chargedCredits = blocks * CreditsPerBlock;

            var availablePrimaryCredits = session.PrimaryLedger == CreditLedgerType.Pro
                ? snapshot.ProAvailableCredits
                : snapshot.PremiumAvailableCredits;
            var consumedPrimaryCredits = Math.Min(availablePrimaryCredits, chargedCredits);
            var shortfall = chargedCredits - consumedPrimaryCredits;
            var premiumDebtAdded = Math.Min(shortfall, ProtectedContinuationCap);

            if (session.PrimaryLedger == CreditLedgerType.Pro)
            {
                snapshot.ProAvailableCredits = Math.Max(0m, snapshot.ProAvailableCredits - consumedPrimaryCredits);
            }
            else
            {
                snapshot.PremiumAvailableCredits = Math.Max(0m, snapshot.PremiumAvailableCredits - consumedPrimaryCredits);
            }

            snapshot.PremiumNegativeCredits += premiumDebtAdded;
            snapshot.HasResumableLockedSession = false;
            snapshot.LastLockedSessionId = string.Empty;
            snapshot.LastLockTokenHash = string.Empty;
            snapshot.LastValidatedAtUtc = DateTime.UtcNow;

            session.State = InterviewSessionState.Completed;
            session.EndedAtUtc = endedAtUtc;
            session.ChargedBlocks = blocks;
            session.ChargedCredits = chargedCredits;
            session.PremiumDebtAdded = premiumDebtAdded;

            _interviewSessionRepository.Save(session);
            _accountCacheRepository.Save(snapshot);

            return new InterviewSessionCompletionResult
            {
                UserId = session.UserId,
                SessionId = session.SessionId,
                StartedAtUtc = session.StartedAtUtc,
                EndedAtUtc = endedAtUtc,
                ChargedBlocks = blocks,
                ChargedCredits = chargedCredits,
                PremiumDebtAdded = premiumDebtAdded,
                RemainingProCredits = snapshot.ProAvailableCredits,
                RemainingPremiumCredits = snapshot.PremiumAvailableCredits
            };
        }

        private static CreditLedgerType? ResolveEligibleLedger(AccountCacheSnapshot snapshot)
        {
            if (snapshot.ProAvailableCredits >= CreditsPerBlock)
            {
                return CreditLedgerType.Pro;
            }

            if (snapshot.PremiumAvailableCredits >= CreditsPerBlock)
            {
                return CreditLedgerType.Premium;
            }

            return null;
        }

        private static InterviewSessionActivationResult Denied(string title, string message)
        {
            return new InterviewSessionActivationResult
            {
                Allowed = false,
                Title = title,
                Message = message
            };
        }

        private static string HashToken(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes);
        }
    }
}
