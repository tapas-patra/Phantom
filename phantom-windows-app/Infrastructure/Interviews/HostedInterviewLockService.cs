using System;
using System.Reflection;
using SecureOverlay.Application.Interviews;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Domain.Enums;
using SecureOverlay.Infrastructure.Hosted;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Interviews
{
    public sealed class HostedInterviewLockService : IInterviewLockService
    {
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan LockTtl = TimeSpan.FromMinutes(5);

        private readonly IInterviewSessionRepository _interviewSessionRepository;
        private readonly IAccountCacheRepository _accountCacheRepository;
        private readonly IAuthSessionRepository _authSessionRepository;
        private readonly IHostedLockClient _hostedLockClient;
        private readonly string _installId;
        private readonly string _appVersion;
        private readonly LocalInterviewLockService _fallback;

        public HostedInterviewLockService(
            IInterviewSessionRepository interviewSessionRepository,
            IAccountCacheRepository accountCacheRepository,
            IAuthSessionRepository authSessionRepository,
            IHostedLockClient hostedLockClient,
            string installId)
        {
            _interviewSessionRepository = interviewSessionRepository;
            _accountCacheRepository = accountCacheRepository;
            _authSessionRepository = authSessionRepository;
            _hostedLockClient = hostedLockClient;
            _installId = installId;
            _appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            _fallback = new LocalInterviewLockService(interviewSessionRepository, accountCacheRepository);
        }

        public InterviewLockHeartbeatResult StartOrResumeLock(InterviewSessionRecord session)
        {
            var authSession = _authSessionRepository.Load();
            if (authSession == null || !authSession.IsAuthenticated || string.IsNullOrWhiteSpace(authSession.AccessToken))
            {
                return _fallback.StartOrResumeLock(session);
            }

            try
            {
                var result = _hostedLockClient.Acquire(new DeviceLockAcquireRequestDto
                {
                    UserId = session.UserId,
                    DeviceId = _installId,
                    SessionId = session.SessionId,
                    AppVersion = _appVersion
                }, authSession.AccessToken);

                if (!result.Acquired)
                {
                    return new InterviewLockHeartbeatResult
                    {
                        Succeeded = false,
                        Title = "Interview Locked Elsewhere",
                        Message = string.IsNullOrWhiteSpace(result.HolderDeviceId)
                            ? "Another device already holds the active interview lock."
                            : $"Another device already holds the active interview lock ({result.HolderDeviceId}).",
                        LockExpiresAtUtc = result.ExpiresAtUtc
                    };
                }

                session.LockTokenHash = result.LockToken;
                session.LastHeartbeatAtUtc = DateTime.UtcNow;
                session.LockExpiresAtUtc = result.ExpiresAtUtc;
                session.HeartbeatIntervalSeconds = (int)HeartbeatInterval.TotalSeconds;
                session.LockTtlSeconds = (int)LockTtl.TotalSeconds;
                _interviewSessionRepository.Save(session);
                SyncAccountLockState(session, hasResumableLock: true);

                return new InterviewLockHeartbeatResult
                {
                    Succeeded = true,
                    Resumed = true,
                    Title = "Interview Lock Active",
                    Message = "The hosted interview lock is active for this device.",
                    LockExpiresAtUtc = result.ExpiresAtUtc
                };
            }
            catch (HostedServiceException ex)
            {
                Log.WriteLine($"Hosted interview lock acquire failed, falling back locally: {ex.Message}");
                return _fallback.StartOrResumeLock(session);
            }
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

            var authSession = _authSessionRepository.Load();
            if (authSession == null || !authSession.IsAuthenticated || string.IsNullOrWhiteSpace(authSession.AccessToken))
            {
                return _fallback.HeartbeatActiveLock();
            }

            if (string.IsNullOrWhiteSpace(session.LockTokenHash))
            {
                return _fallback.HeartbeatActiveLock();
            }

            try
            {
                var result = _hostedLockClient.Heartbeat(new DeviceLockHeartbeatRequestDto
                {
                    SessionId = session.SessionId,
                    DeviceId = _installId,
                    LockToken = session.LockTokenHash
                }, authSession.AccessToken);

                session.LastHeartbeatAtUtc = DateTime.UtcNow;
                session.LockExpiresAtUtc = result.ExpiresAtUtc;
                _interviewSessionRepository.Save(session);
                SyncAccountLockState(session, hasResumableLock: true);

                return new InterviewLockHeartbeatResult
                {
                    Succeeded = true,
                    Title = "Lock Heartbeat Sent",
                    Message = "The hosted interview lock heartbeat refreshed successfully.",
                    LockExpiresAtUtc = result.ExpiresAtUtc
                };
            }
            catch (HostedServiceException ex)
            {
                Log.WriteLine($"Hosted interview lock heartbeat failed, falling back locally: {ex.Message}");
                return _fallback.HeartbeatActiveLock();
            }
        }

        public void MarkLockReleased()
        {
            try
            {
                var session = _interviewSessionRepository.Load();
                var authSession = _authSessionRepository.Load();
                if (session != null
                    && authSession != null
                    && authSession.IsAuthenticated
                    && !string.IsNullOrWhiteSpace(authSession.AccessToken)
                    && !string.IsNullOrWhiteSpace(session.LockTokenHash))
                {
                    _hostedLockClient.Release(new DeviceLockReleaseRequestDto
                    {
                        SessionId = session.SessionId,
                        LockToken = session.LockTokenHash,
                        ReleaseReason = "desktop_cleanup"
                    }, authSession.AccessToken);
                }
            }
            catch (HostedServiceException ex)
            {
                Log.WriteLine($"Hosted interview lock release failed: {ex.Message}");
            }

            _fallback.MarkLockReleased();
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
