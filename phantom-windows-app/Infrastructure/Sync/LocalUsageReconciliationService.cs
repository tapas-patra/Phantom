using System;
using System.Linq;
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
        private readonly IHostedUsageClient _hostedUsageClient;

        public LocalUsageReconciliationService(
            IUsageReconciliationRepository repository,
            IHostedUsageClient hostedUsageClient)
        {
            _repository = repository;
            _hostedUsageClient = hostedUsageClient;
        }

        public void Enqueue(UsageReconciliationPayload payload)
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

        public UsageReconciliationFlushResult FlushPending()
        {
            var records = _repository.LoadAll();
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
                        PremiumDebtAdded = record.Payload.PremiumDebtAdded
                    });

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

            _repository.SaveAll(records);
            return result;
        }

        public UsageReconciliationQueueSnapshot GetQueueSnapshot()
        {
            var records = _repository.LoadAll();
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
