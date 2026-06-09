using System.Collections.Generic;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Application.Persistence
{
    public interface ITelemetryRepository
    {
        List<TelemetryEvent> LoadAll();
        void SaveAll(List<TelemetryEvent> events);
    }
}
