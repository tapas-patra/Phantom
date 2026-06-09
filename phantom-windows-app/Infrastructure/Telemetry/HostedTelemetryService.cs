using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly IHostedTelemetryClient _hostedTelemetryClient;
        private readonly HostedRuntimeOptions _hostedRuntimeOptions;

        public HostedTelemetryService(
            ITelemetryRepository repository,
            IHostedTelemetryClient hostedTelemetryClient,
            HostedRuntimeOptions hostedRuntimeOptions)
        {
            _repository = repository;
            _hostedTelemetryClient = hostedTelemetryClient;
            _hostedRuntimeOptions = hostedRuntimeOptions;
        }

        public void Track(string category, string eventName, Dictionary<string, string>? attributes = null)
        {
            var events = _repository.LoadAll();
            events.Add(new TelemetryEvent
            {
                EventId = $"telemetry-{Guid.NewGuid():N}",
                Category = category,
                EventName = eventName,
                OccurredAtUtc = DateTime.UtcNow,
                Attributes = attributes ?? new Dictionary<string, string>()
            });

            events = Trim(events);
            _repository.SaveAll(events);

            if (_hostedRuntimeOptions.UseRemoteBackend)
            {
                FlushPending(events);
            }
        }

        public IReadOnlyList<TelemetryEvent> GetRecent(int maxCount = 50)
        {
            return _repository.LoadAll()
                .OrderByDescending(item => item.OccurredAtUtc)
                .Take(Math.Max(1, maxCount))
                .ToList();
        }

        private void FlushPending(List<TelemetryEvent> events)
        {
            var remaining = new List<TelemetryEvent>();
            foreach (var telemetryEvent in events.OrderBy(item => item.OccurredAtUtc))
            {
                try
                {
                    _hostedTelemetryClient.Ingest(telemetryEvent);
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"Telemetry flush deferred: {ex.Message}");
                    remaining.Add(telemetryEvent);
                }
            }

            _repository.SaveAll(Trim(remaining));
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
