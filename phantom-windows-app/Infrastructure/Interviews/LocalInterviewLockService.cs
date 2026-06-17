using System;
using SecureOverlay.Application.Interviews;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Domain.Enums;

namespace SecureOverlay.Infrastructure.Interviews
{
    public sealed class LocalInterviewLockService : IInterviewLockService
    {
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan LockTtl = TimeSpan.FromMinutes(5);

        private readonly IInterviewSessionRepository _interviewSessionRepository;
        private readonly IAccountCacheRepository _accountCacheRepository;

        public LocalInterviewLockService(
            IInterviewSessionRepository interviewSessionRepository,
            IAccountCacheRepository accountCacheRepository)
        {
            _interviewSessionRepository = interviewSessionRepository;
            _accountCacheRepository = accountCacheRepository;
        }

        public InterviewLockHeartbeatResult StartOrResumeLock(InterviewSessionRecord session)
        {
            session.LastHeartbeatAtUtc = DateTime.UtcNow;
            session.LockExpiresAtUtc = session.LastHeartbeatAtUtc.Add(LockTtl);
            session.HeartbeatIntervalSeconds = (int)HeartbeatInterval.TotalSeconds;
            session.LockTtlSeconds = (int)LockTtl.TotalSeconds;
            _interviewSessionRepository.Save(session);
            SyncAccountLockState(session, hasResumableLock: true);

            return new InterviewLockHeartbeatResult
            {
                Succeeded = true,
                Resumed = true,
                Title = "Interview Lock Active",
                Message = "The local session lock is active for this device.",
                LockExpiresAtUtc = session.LockExpiresAtUtc
            };
        }

        public InterviewLockHeartbeatResult HeartbeatActiveLock()
        {
            var session = _interviewSessionRepository.Load();
            if (session == null || (session.State != InterviewSessionState.Active && session.State != InterviewSessionState.Paused))
            {
                return new InterviewLockHeartbeatResult
                {
                    Succeeded = false,
                    Title = "No Active Lock",
                    Message = "No active interview session was found to heartbeat."
                };
            }

            session.LastHeartbeatAtUtc = DateTime.UtcNow;
            session.LockExpiresAtUtc = session.LastHeartbeatAtUtc.AddSeconds(session.LockTtlSeconds > 0 ? session.LockTtlSeconds : (int)LockTtl.TotalSeconds);
            _interviewSessionRepository.Save(session);
            SyncAccountLockState(session, hasResumableLock: true);

            return new InterviewLockHeartbeatResult
            {
                Succeeded = true,
                Title = "Lock Heartbeat Sent",
                Message = "The interview lock heartbeat refreshed successfully.",
                LockExpiresAtUtc = session.LockExpiresAtUtc
            };
        }

        public void MarkLockReleased()
        {
            var snapshot = _accountCacheRepository.Load();
            if (snapshot == null)
            {
                return;
            }

            snapshot.HasResumableLockedSession = false;
            snapshot.LastLockTokenHash = string.Empty;
            snapshot.LastLockedSessionId = string.Empty;
            snapshot.LastValidatedAtUtc = DateTime.UtcNow;
            _accountCacheRepository.Save(snapshot);
        }

        private void SyncAccountLockState(InterviewSessionRecord session, bool hasResumableLock)
        {
            var snapshot = _accountCacheRepository.Load();
            if (snapshot == null)
            {
                return;
            }

            snapshot.HasResumableLockedSession = hasResumableLock;
            snapshot.LastLockTokenHash = session.LockTokenHash;
            snapshot.LastLockedSessionId = session.SessionId;
            snapshot.LastValidatedAtUtc = DateTime.UtcNow;
            _accountCacheRepository.Save(snapshot);
        }
    }
}
