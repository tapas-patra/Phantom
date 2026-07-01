using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Application.Telemetry;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Infrastructure.Hosted;

namespace SecureOverlay.Infrastructure.Telemetry
{
    public sealed class HostedTelemetryService : ITelemetryService
    {
        private const int MaxStoredEvents = 500;

        private readonly ITelemetryRepository _repository;
        private readonly IAuthSessionRepository _authSessionRepository;
        private readonly IHostedTelemetryClient _hostedTelemetryClient;
        private readonly HostedRuntimeOptions _hostedRuntimeOptions;
        private readonly object _syncLock = new object();
        private int _flushScheduled;

        public HostedTelemetryService(
            ITelemetryRepository repository,
            IAuthSessionRepository authSessionRepository,
            IHostedTelemetryClient hostedTelemetryClient,
            HostedRuntimeOptions hostedRuntimeOptions)
        {
            _repository = repository;
            _authSessionRepository = authSessionRepository;
            _hostedTelemetryClient = hostedTelemetryClient;
            _hostedRuntimeOptions = hostedRuntimeOptions;
        }

        public void Track(string category, string eventName, Dictionary<string, string>? attributes = null)
        {
            var telemetryEvent = new TelemetryEvent
            {
                EventId = $"telemetry-{Guid.NewGuid():N}",
                Category = category,
                EventName = eventName,
                OccurredAtUtc = DateTime.UtcNow,
                Attributes = attributes ?? new Dictionary<string, string>()
            };

            lock (_syncLock)
            {
                var events = _repository.LoadAll();
                events.Add(telemetryEvent);
                _repository.SaveAll(Trim(events));
            }

            if (_hostedRuntimeOptions.UseRemoteBackend)
            {
                ScheduleFlush();
            }
        }

        public IReadOnlyList<TelemetryEvent> GetRecent(int maxCount = 50)
        {
            return _repository.LoadAll()
                .OrderByDescending(item => item.OccurredAtUtc)
                .Take(Math.Max(1, maxCount))
                .ToList();
        }

        private void ScheduleFlush()
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
                finally
                {
                    Interlocked.Exchange(ref _flushScheduled, 0);
                }
            });
        }

        private void FlushPending()
        {
            List<TelemetryEvent> queuedEvents;
            lock (_syncLock)
            {
                queuedEvents = _repository.LoadAll()
                    .OrderBy(item => item.OccurredAtUtc)
                    .ToList();
            }

            if (queuedEvents.Count == 0)
            {
                return;
            }

            var session = _authSessionRepository.Load();
            if (session == null
                || !session.IsAuthenticated
                || string.IsNullOrWhiteSpace(session.AccessToken))
            {
                return;
            }

            var succeededIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var telemetryEvent in queuedEvents)
            {
                try
                {
                    _hostedTelemetryClient.Ingest(telemetryEvent, session.AccessToken);
                    succeededIds.Add(telemetryEvent.EventId);
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"Telemetry flush deferred: {ex.Message}");
                    break;
                }
            }

            if (succeededIds.Count == 0)
            {
                return;
            }

            lock (_syncLock)
            {
                var remaining = _repository.LoadAll()
                    .Where(item => !succeededIds.Contains(item.EventId))
                    .ToList();
                _repository.SaveAll(Trim(remaining));
            }
        }

        private static List<TelemetryEvent> Trim(List<TelemetryEvent> events)
        {
            if (events.Count <= MaxStoredEvents)
            {
                return events;
            }

            return events
                .OrderByDescending(item => item.OccurredAtUtc)
                .Take(MaxStoredEvents)
                .OrderBy(item => item.OccurredAtUtc)
                .ToList();
        }
    }
}
