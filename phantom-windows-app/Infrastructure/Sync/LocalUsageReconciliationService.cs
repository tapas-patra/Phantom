using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Application.Sync;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Domain.Enums;
using SecureOverlay.Infrastructure.Hosted;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Sync
{
    public sealed class LocalUsageReconciliationService : IUsageReconciliationService
    {
        private const int MaxAttemptsBeforeDeadLetter = 3;

        private readonly IUsageReconciliationRepository _repository;
        private readonly IAuthSessionRepository _authSessionRepository;
        private readonly IHostedUsageClient _hostedUsageClient;
        private readonly object _syncLock = new object();
        private int _flushScheduled;

        public LocalUsageReconciliationService(
            IUsageReconciliationRepository repository,
            IAuthSessionRepository authSessionRepository,
            IHostedUsageClient hostedUsageClient)
        {
            _repository = repository;
            _authSessionRepository = authSessionRepository;
            _hostedUsageClient = hostedUsageClient;
        }

        public void Enqueue(UsageReconciliationPayload payload)
        {
            lock (_syncLock)
            {
                var records = _repository.LoadAll();
                records.Add(new UsageReconciliationRecord
                {
                    RecordId = $"usage-{Guid.NewGuid():N}",
                    Payload = payload,
                    Status = UsageSyncStatus.Pending,
                    CreatedAtUtc = DateTime.UtcNow
                });
                _repository.SaveAll(records);
            }

            FlushPendingInBackground();
        }

        public UsageReconciliationFlushResult FlushPending()
        {
            List<UsageReconciliationRecord> records;
            lock (_syncLock)
            {
                records = _repository.LoadAll();
            }

            var result = new UsageReconciliationFlushResult
            {
                PendingBefore = records.Count(record => record.Status == UsageSyncStatus.Pending || record.Status == UsageSyncStatus.Failed)
            };

            foreach (var record in records.Where(record => record.Status == UsageSyncStatus.Pending || record.Status == UsageSyncStatus.Failed))
            {
                if (record.AttemptCount >= MaxAttemptsBeforeDeadLetter)
                {
                    record.Status = UsageSyncStatus.DeadLetter;
                    record.DeadLetteredAtUtc = DateTime.UtcNow;
                    record.LastError = string.IsNullOrWhiteSpace(record.LastError)
                        ? "Usage reconciliation exceeded the retry limit."
                        : record.LastError;
                    result.FailedCount += 1;
                    continue;
                }

                try
                {
                    var session = _authSessionRepository.Load();
                    if (session == null || !session.IsAuthenticated || string.IsNullOrWhiteSpace(session.AccessToken))
                    {
                        MarkFailed(record, "No authenticated desktop session is available for usage reconciliation.");
                        result.FailedCount += 1;
                        continue;
                    }

                    record.AttemptCount += 1;
                    record.LastAttemptAtUtc = DateTime.UtcNow;

                    var response = _hostedUsageClient.Reconcile(new UsageReconciliationRequestDto
                    {
                        UserId = record.Payload.UserId,
                        SessionId = record.Payload.SessionId,
                        StartedAtUtc = record.Payload.StartedAtUtc,
                        EndedAtUtc = record.Payload.EndedAtUtc,
                        ChargedCredits = record.Payload.ChargedCredits,
                        ChargedBlocks = record.Payload.ChargedBlocks,
                        ConsumedProCredits = record.Payload.ConsumedProCredits,
                        ConsumedPremiumCredits = record.Payload.ConsumedPremiumCredits,
                        PremiumDebtAdded = record.Payload.PremiumDebtAdded
                    }, session.AccessToken);

                    if (response.Accepted)
                    {
                        record.Status = UsageSyncStatus.Synced;
                        record.LastError = string.Empty;
                        record.LedgerEntryId = response.LedgerEntryId;
                        record.SyncedAtUtc = DateTime.UtcNow;
                        result.SyncedCount += 1;
                    }
                    else
                    {
                        MarkFailed(record, "Hosted usage reconciliation rejected the session.");
                        result.FailedCount += 1;
                    }
                }
                catch (Exception ex)
                {
                    MarkFailed(record, ex.Message);
                    result.FailedCount += 1;
                }
            }

            lock (_syncLock)
            {
                var latest = _repository.LoadAll();
                var updatedById = records.ToDictionary(record => record.RecordId, StringComparer.Ordinal);
                foreach (var latestRecord in latest)
                {
                    if (updatedById.TryGetValue(latestRecord.RecordId, out var updated))
                    {
                        latestRecord.Status = updated.Status;
                        latestRecord.AttemptCount = updated.AttemptCount;
                        latestRecord.LastError = updated.LastError;
                        latestRecord.LedgerEntryId = updated.LedgerEntryId;
                        latestRecord.LastAttemptAtUtc = updated.LastAttemptAtUtc;
                        latestRecord.SyncedAtUtc = updated.SyncedAtUtc;
                        latestRecord.DeadLetteredAtUtc = updated.DeadLetteredAtUtc;
                    }
                }

                _repository.SaveAll(latest);
            }

            return result;
        }

        public void FlushPendingInBackground()
        {
            if (Interlocked.Exchange(ref _flushScheduled, 1) == 1)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    FlushPending();
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"Background usage reconciliation failed: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _flushScheduled, 0);
                }
            });
        }

        public UsageReconciliationQueueSnapshot GetQueueSnapshot()
        {
            List<UsageReconciliationRecord> records;
            lock (_syncLock)
            {
                records = _repository.LoadAll();
            }

            return new UsageReconciliationQueueSnapshot
            {
                PendingCount = records.Count(record => record.Status == UsageSyncStatus.Pending),
                FailedCount = records.Count(record => record.Status == UsageSyncStatus.Failed),
                DeadLetterCount = records.Count(record => record.Status == UsageSyncStatus.DeadLetter),
                DeadLetters = records
                    .Where(record => record.Status == UsageSyncStatus.DeadLetter)
                    .OrderByDescending(record => record.DeadLetteredAtUtc ?? record.LastAttemptAtUtc ?? record.CreatedAtUtc)
                    .ToList()
            };
        }

        private static void MarkFailed(UsageReconciliationRecord record, string error)
        {
            if (record.AttemptCount >= MaxAttemptsBeforeDeadLetter)
            {
                record.Status = UsageSyncStatus.DeadLetter;
                record.DeadLetteredAtUtc = DateTime.UtcNow;
            }
            else
            {
                record.Status = UsageSyncStatus.Failed;
            }

            record.LastError = error;
        }
    }
}
