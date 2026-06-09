using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Infrastructure.Hosted
{
    public sealed class HttpHostedTelemetryClient : HttpHostedClientBase, IHostedTelemetryClient
    {
        public HttpHostedTelemetryClient(HostedRuntimeOptions options)
            : base(options)
        {
        }

        public void Ingest(TelemetryEvent telemetryEvent)
        {
            PostJson<object, object>(
                "/api/desktop/telemetry/ingest",
                new
                {
                    category = telemetryEvent.Category,
                    eventName = telemetryEvent.EventName,
                    attributes = telemetryEvent.Attributes,
                    occurredAtUtc = telemetryEvent.OccurredAtUtc
                });
        }
    }
}
