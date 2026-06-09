using System;
using System.Collections.Generic;
using System.Linq;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Application.Telemetry;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Infrastructure.Telemetry
{
    public sealed class LocalTelemetryService : ITelemetryService
    {
        private const int MaxStoredEvents = 500;

        private readonly ITelemetryRepository _repository;

        public LocalTelemetryService(ITelemetryRepository repository)
        {
            _repository = repository;
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

            if (events.Count > MaxStoredEvents)
            {
                events = events
                    .OrderByDescending(item => item.OccurredAtUtc)
                    .Take(MaxStoredEvents)
                    .OrderBy(item => item.OccurredAtUtc)
                    .ToList();
            }

            _repository.SaveAll(events);
        }

        public IReadOnlyList<TelemetryEvent> GetRecent(int maxCount = 50)
        {
            return _repository.LoadAll()
                .OrderByDescending(item => item.OccurredAtUtc)
                .Take(Math.Max(1, maxCount))
                .ToList();
        }
    }
}
