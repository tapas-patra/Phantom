using System.Collections.Generic;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Application.Telemetry
{
    public interface ITelemetryService
    {
        void Track(string category, string eventName, Dictionary<string, string>? attributes = null);
        IReadOnlyList<TelemetryEvent> GetRecent(int maxCount = 50);
    }
}
