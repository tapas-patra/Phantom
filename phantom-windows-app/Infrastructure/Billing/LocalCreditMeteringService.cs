using System;
using System.Collections.Generic;
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
        private const decimal CreditsPerMinute = 1m / 60m;
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
            var existingSession = LoadUsableActiveSession();
            if (existingSession != null
                && (existingSession.State == InterviewSessionState.Active || existingSession.State == InterviewSessionState.Paused))
            {
                var isPaused = existingSession.State == InterviewSessionState.Paused;
                return new InterviewSessionActivationResult
                {
                    Allowed = true,
                    StartedNewSession = false,
                    ResumedExistingSession = true,
                    Title = isPaused ? "Interview Paused" : "Interview Resumed",
                    Message = isPaused
                        ? "The current locked interview is paused on this device and will remain paused until a later response succeeds."
                        : "Resuming the current locked interview session on this device.",
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
                if (IsFreeTier(snapshot))
                {
                    return Denied("Free Trial Exhausted", "Some free-trial credit must be available before a new interview starts.");
                }

                return Denied("No Credits Available", "Some paid credit must be available before a new interview starts.");
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
            return LoadUsableActiveSession();
        }

        public TimeSpan GetMeteredElapsed(InterviewSessionRecord session)
        {
            var effectiveEnd = session.State == InterviewSessionState.Completed
                ? (session.EndedAtUtc ?? DateTime.UtcNow)
                : DateTime.UtcNow;
            var pausedSeconds = session.TotalPausedSeconds;
            if (session.State == InterviewSessionState.Paused && session.PausedAtUtc.HasValue)
            {
                pausedSeconds += Math.Max(0, (int)Math.Floor((effectiveEnd - session.PausedAtUtc.Value).TotalSeconds));
            }

            var metered = effectiveEnd - session.StartedAtUtc - TimeSpan.FromSeconds(pausedSeconds);
            return metered < TimeSpan.Zero ? TimeSpan.Zero : metered;
        }

        public bool TrackUsageSource(InterviewUsageSource source, string providerId)
        {
            var session = _interviewSessionRepository.Load();
            if (session == null || (session.State != InterviewSessionState.Active && session.State != InterviewSessionState.Paused))
            {
                return false;
            }

            session.UsageSegments ??= new List<InterviewSessionUsageSegment>();
            var currentMeteredSecond = Math.Max(0, (int)Math.Floor(GetMeteredElapsed(session).TotalSeconds));
            var lastSegment = session.UsageSegments.Count > 0
                ? session.UsageSegments[^1]
                : null;

            if (lastSegment != null
                && lastSegment.Source == source
                && string.Equals(lastSegment.ProviderId, providerId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && !lastSegment.EndedMeteredSecond.HasValue)
            {
                return false;
            }

            if (lastSegment != null && !lastSegment.EndedMeteredSecond.HasValue)
            {
                lastSegment.EndedMeteredSecond = currentMeteredSecond;
            }

            session.UsageSegments.Add(new InterviewSessionUsageSegment
            {
                Source = source,
                ProviderId = providerId ?? string.Empty,
                StartedMeteredSecond = currentMeteredSecond
            });
            _interviewSessionRepository.Save(session);
            return true;
        }

        public bool PauseActiveSession()
        {
            var session = _interviewSessionRepository.Load();
            if (session == null || session.State != InterviewSessionState.Active)
            {
                return false;
            }

            session.State = InterviewSessionState.Paused;
            session.PausedAtUtc = DateTime.UtcNow;
            _interviewSessionRepository.Save(session);
            return true;
        }

        public bool ResumePausedSession()
        {
            var session = _interviewSessionRepository.Load();
            if (session == null || session.State != InterviewSessionState.Paused || !session.PausedAtUtc.HasValue)
            {
                return false;
            }

            session.TotalPausedSeconds += Math.Max(0, (int)Math.Floor((DateTime.UtcNow - session.PausedAtUtc.Value).TotalSeconds));
            session.PausedAtUtc = null;
            session.State = InterviewSessionState.Active;
            session.LastHeartbeatAtUtc = DateTime.UtcNow;
            _interviewSessionRepository.Save(session);
            return true;
        }

        public InterviewSessionCompletionResult? FinalizeActiveSession()
        {
            var session = _interviewSessionRepository.Load();
            var snapshot = _accountCacheRepository.Load();
            if (session == null || snapshot == null ||
                (session.State != InterviewSessionState.Active && session.State != InterviewSessionState.Paused))
            {
                return null;
            }

            var endedAtUtc = DateTime.UtcNow;
            var duration = GetMeteredElapsed(session);
            var blocks = Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes / MeteringBlock.TotalMinutes));
            var requestedCharge = EstimateChargeForElapsed(duration);
            var totalMeteredSeconds = Math.Max(0, (int)Math.Floor(duration.TotalSeconds));
            CloseOpenUsageSegment(session, totalMeteredSeconds);

            var chargesBySource = AllocateChargeBySource(session, requestedCharge, totalMeteredSeconds);
            var proByoCharge = chargesBySource.TryGetValue(InterviewUsageSource.ProByo, out var proCharge) ? proCharge : 0m;
            var premiumManagedCharge = chargesBySource.TryGetValue(InterviewUsageSource.PremiumManaged, out var managedCharge) ? managedCharge : 0m;
            var premiumExtensionCharge = chargesBySource.TryGetValue(InterviewUsageSource.PremiumDebtExtension, out var extensionCharge) ? extensionCharge : 0m;
            var freeTrialManagedCharge = chargesBySource.TryGetValue(InterviewUsageSource.FreeTrialManaged, out var freeCharge) ? freeCharge : 0m;

            var consumedProCredits = Math.Min(snapshot.ProAvailableCredits, proByoCharge);
            var consumedPremiumCredits = Math.Min(snapshot.PremiumAvailableCredits, premiumManagedCharge);
            var primaryShortfall = Math.Max(0m, proByoCharge - consumedProCredits) + Math.Max(0m, premiumManagedCharge - consumedPremiumCredits);
            var premiumDebtAdded = 0m;

            if (!IsFreeTier(snapshot))
            {
                var remainingDebtBudget = Math.Max(0m, ProtectedContinuationCap - snapshot.PremiumNegativeCredits);

                // Convert any shortfall (when available credits are insufficient) into debt.
                if (primaryShortfall > 0m)
                {
                    var shortfallDebt = Math.Min(primaryShortfall, remainingDebtBudget);
                    premiumDebtAdded += shortfallDebt;
                    remainingDebtBudget -= shortfallDebt;
                }

                // Add explicit extension charge to debt rather than credit balance.
                if (premiumExtensionCharge > 0m && remainingDebtBudget > 0m)
                {
                    premiumDebtAdded += Math.Min(premiumExtensionCharge, remainingDebtBudget);
                }
            }

            var chargedCredits = IsFreeTier(snapshot)
                ? Math.Min(session.PrimaryLedger == CreditLedgerType.Pro ? snapshot.ProAvailableCredits : snapshot.PremiumAvailableCredits, requestedCharge)
                : consumedProCredits + consumedPremiumCredits + premiumDebtAdded;

            if (IsFreeTier(snapshot))
            {
                if (session.PrimaryLedger == CreditLedgerType.Pro)
                {
                    snapshot.ProAvailableCredits = Math.Max(0m, snapshot.ProAvailableCredits - chargedCredits);
                }
                else
                {
                    snapshot.PremiumAvailableCredits = Math.Max(0m, snapshot.PremiumAvailableCredits - chargedCredits);
                }
            }
            else
            {
                snapshot.ProAvailableCredits = Math.Max(0m, snapshot.ProAvailableCredits - consumedProCredits);
                snapshot.PremiumAvailableCredits = Math.Max(0m, snapshot.PremiumAvailableCredits - consumedPremiumCredits);
            }

            snapshot.PremiumNegativeCredits += premiumDebtAdded;
            snapshot.HasResumableLockedSession = false;
            snapshot.LastLockedSessionId = string.Empty;
            snapshot.LastLockTokenHash = string.Empty;
            snapshot.LastValidatedAtUtc = DateTime.UtcNow;

            session.State = InterviewSessionState.Completed;
            session.EndedAtUtc = endedAtUtc;
            session.TotalPausedSeconds = (int)Math.Floor(duration.TotalSeconds < 0 ? 0 : (endedAtUtc - session.StartedAtUtc - duration).TotalSeconds);
            session.PausedAtUtc = null;
            session.ChargedBlocks = blocks;
            session.ChargedCredits = chargedCredits;
            session.PremiumDebtAdded = premiumDebtAdded;
            session.LastHeartbeatAtUtc = endedAtUtc;

            _interviewSessionRepository.Save(session);
            _accountCacheRepository.Save(snapshot);

            System.Diagnostics.Debug.WriteLine(
                $"Interview billing summary: session={session.SessionId}, total={requestedCharge:0.##}, " +
                $"free_managed={freeTrialManagedCharge:0.##}, pro_byo={proByoCharge:0.##}, " +
                $"premium_managed={premiumManagedCharge:0.##}, premium_extension={premiumExtensionCharge:0.##}, " +
                $"consumed_pro={consumedProCredits:0.##}, consumed_premium={consumedPremiumCredits:0.##}, debt={premiumDebtAdded:0.##}");

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

        public void AbandonActiveSession()
        {
            var session = _interviewSessionRepository.Load();
            if (session == null || (session.State != InterviewSessionState.Active && session.State != InterviewSessionState.Paused))
            {
                return;
            }

            var wasPaused = session.State == InterviewSessionState.Paused;
            session.State = InterviewSessionState.Completed;
            session.EndedAtUtc = DateTime.UtcNow;
            if (wasPaused && session.PausedAtUtc.HasValue)
            {
                session.TotalPausedSeconds += Math.Max(0, (int)Math.Floor((DateTime.UtcNow - session.PausedAtUtc.Value).TotalSeconds));
            }
            session.PausedAtUtc = null;
            session.ChargedBlocks = 0;
            session.ChargedCredits = 0m;
            session.PremiumDebtAdded = 0m;
            _interviewSessionRepository.Save(session);

            var snapshot = _accountCacheRepository.Load();
            if (snapshot != null)
            {
                snapshot.HasResumableLockedSession = false;
                snapshot.LastLockedSessionId = string.Empty;
                snapshot.LastLockTokenHash = string.Empty;
                snapshot.LastValidatedAtUtc = DateTime.UtcNow;
                _accountCacheRepository.Save(snapshot);
            }
        }

        private static CreditLedgerType? ResolveEligibleLedger(AccountCacheSnapshot snapshot)
        {
            if (!IsFreeTier(snapshot) && snapshot.PremiumAvailableCredits > 0m)
            {
                return CreditLedgerType.Premium;
            }

            if (snapshot.ProAvailableCredits > 0m)
            {
                return CreditLedgerType.Pro;
            }

            if (snapshot.PremiumAvailableCredits > 0m)
            {
                return CreditLedgerType.Premium;
            }

            return null;
        }

        private static bool IsFreeTier(AccountCacheSnapshot snapshot)
        {
            return string.Equals(snapshot.AccessTier, "free", StringComparison.OrdinalIgnoreCase);
        }

        private static void CloseOpenUsageSegment(InterviewSessionRecord session, int meteredSeconds)
        {
            session.UsageSegments ??= new List<InterviewSessionUsageSegment>();
            if (session.UsageSegments.Count == 0)
            {
                return;
            }

            var lastSegment = session.UsageSegments[^1];
            if (!lastSegment.EndedMeteredSecond.HasValue)
            {
                lastSegment.EndedMeteredSecond = meteredSeconds;
            }
        }

        private static Dictionary<InterviewUsageSource, decimal> AllocateChargeBySource(
            InterviewSessionRecord session,
            decimal requestedCharge,
            int totalMeteredSeconds)
        {
            session.UsageSegments ??= new List<InterviewSessionUsageSegment>();
            var secondsBySource = new Dictionary<InterviewUsageSource, int>();
            var orderedSources = new List<InterviewUsageSource>();

            foreach (var segment in session.UsageSegments)
            {
                var segmentEnd = segment.EndedMeteredSecond ?? totalMeteredSeconds;
                var seconds = Math.Max(0, segmentEnd - segment.StartedMeteredSecond);
                if (seconds == 0)
                {
                    continue;
                }

                if (!secondsBySource.ContainsKey(segment.Source))
                {
                    secondsBySource[segment.Source] = 0;
                    orderedSources.Add(segment.Source);
                }

                secondsBySource[segment.Source] += seconds;
            }

            if (secondsBySource.Count == 0)
            {
                var fallbackSource = session.PrimaryLedger == CreditLedgerType.Pro
                    ? InterviewUsageSource.ProByo
                    : InterviewUsageSource.PremiumManaged;
                secondsBySource[fallbackSource] = Math.Max(1, totalMeteredSeconds);
                orderedSources.Add(fallbackSource);
            }

            var allocated = new Dictionary<InterviewUsageSource, decimal>();
            var remainingCharge = requestedCharge;
            var totalSeconds = 0;
            foreach (var seconds in secondsBySource.Values)
            {
                totalSeconds += seconds;
            }

            for (var index = 0; index < orderedSources.Count; index++)
            {
                var source = orderedSources[index];
                if (index == orderedSources.Count - 1)
                {
                    allocated[source] = RoundCredits(Math.Max(0m, remainingCharge));
                    break;
                }

                var proportionalCharge = totalSeconds <= 0
                    ? 0m
                    : RoundDownCredits(requestedCharge * secondsBySource[source] / totalSeconds);
                proportionalCharge = Math.Min(proportionalCharge, remainingCharge);
                allocated[source] = proportionalCharge;
                remainingCharge -= proportionalCharge;
            }

            return allocated;
        }

        public static decimal EstimateChargeForElapsed(TimeSpan elapsed)
        {
            return RoundCredits(GetCompletedMinutes(elapsed) * CreditsPerMinute);
        }

        private static int GetCompletedMinutes(TimeSpan duration)
        {
            return Math.Max(0, (int)Math.Floor(duration.TotalSeconds / 60d));
        }

        private static decimal RoundDownCredits(decimal credits)
        {
            return Math.Floor(credits * 100m) / 100m;
        }

        private static decimal RoundCredits(decimal credits)
        {
            return Math.Round(credits, 2, MidpointRounding.AwayFromZero);
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

        private InterviewSessionRecord? LoadUsableActiveSession()
        {
            var session = _interviewSessionRepository.Load();
            if (session == null || (session.State != InterviewSessionState.Active && session.State != InterviewSessionState.Paused))
            {
                return null;
            }

            var lockExpiresAtUtc = session.LockExpiresAtUtc
                ?? session.LastHeartbeatAtUtc.AddSeconds(session.LockTtlSeconds > 0 ? session.LockTtlSeconds : 300);
            if (lockExpiresAtUtc > DateTime.UtcNow)
            {
                return session;
            }

            session.State = InterviewSessionState.Completed;
            session.EndedAtUtc = session.EndedAtUtc ?? DateTime.UtcNow;
            _interviewSessionRepository.Save(session);

            var snapshot = _accountCacheRepository.Load();
            if (snapshot != null)
            {
                snapshot.HasResumableLockedSession = false;
                snapshot.LastLockedSessionId = string.Empty;
                snapshot.LastLockTokenHash = string.Empty;
                snapshot.LastValidatedAtUtc = DateTime.UtcNow;
                _accountCacheRepository.Save(snapshot);
            }

            return null;
        }
    }
}
