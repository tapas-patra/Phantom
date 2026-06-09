using System;
using System.Collections.Generic;

namespace SecureOverlay.Domain.Entities
{
    public sealed class TelemetryEvent
    {
        public string EventId { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public DateTime OccurredAtUtc { get; set; }
        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
    }
}
