# Phantom observability

Phantom uses the logging already built into each runtime: .NET `ILogger` to stdout on both hosted services, `LiveRequestTrace` on Windows, and `Diagnostics` plus the existing telemetry queue on macOS. No prompt, answer, transcript, résumé, email, phone, API key, access token, document ID, query text, clipboard data, image, connection string, or file path is logged.

## Render

Open the Render service, choose **Logs**, and search the JSON output by a correlation value copied from the desktop diagnostics. The Windows authority service and dashboard query service emit one-line JSON and suppress successful `/health` polling plus routine ASP.NET framework information logs.

Useful searches:

- `request_started` or `request_completed` — inbound service request boundary.
- `request_rate_limited` — a request was rejected with HTTP 429; correlate it with the matching request boundary.
- `component=managed_ai` — managed provider dispatch, headers, first upstream token, completion, timeout, or failure.
- `provider_first_token_timeout` — provider produced no token inside the 12-second deadline and the existing retry path took over.
- `component=desktop_telemetry` — content-free macOS or Windows client milestones received by the authority backend.
- `component=authority_client` — dashboard-to-authority attempts, retries, status class, and latency.
- `component=telemetry` — aggregate telemetry batch persistence.
- `event=provider_retry_started` — a retry stayed inside the selected execution lane.
- `event=provider_rotated` — the next managed credential or local BYO key slot was selected; key values and stable BYO key identifiers are never emitted.
- `event=execution_lane_changed` — an explicitly opted-in BYO request moved to managed extension before any provider output was received.

For one live turn, search its `turn_id`. For one provider attempt, search its `operation_id` or `request_id`. The expected managed chain is:

`request_started → provider_request_started → provider_headers_received → first_upstream_token → first_backend_sse_write → provider_stream_completed → request_completed`

Managed desktop clients dispatch once; the authority backend owns at most two credential attempts and persists rate-limit, authentication, and transient cooldowns in PostgreSQL so multiple Render instances share health state. BYO calls never send their keys to Render: Windows stores them in its encrypted vault, macOS stores them in Keychain, and each native client performs at most two local attempts. Neither lane retries or crosses lanes after output begins. BYO-to-managed fallback requires the paid-extension opt-in and records the new usage segment before dispatch.

If `provider_headers_received` is slow, the delay is connection/provider admission. If headers are fast but `first_upstream_token` is slow, it is provider generation latency. If backend writes quickly but `model_first_byte_received` is late, inspect the desktop control-frame/parser path.

Desktop telemetry also carries an opaque `event_id`. Replayed queue entries keep the same ID, so the authority service acknowledges them idempotently and does not persist or print duplicate milestone records. `telemetry_batch_flushed` confirms that the bounded backend channel reached PostgreSQL; accepted ingest requests may precede that message by the two-second batching window.

## Desktop files

- Windows: `%AppData%\Windows Host Service 271\performance_log.jsonl` and `crash_log.txt`.
- macOS: `~/Library/Application Support/Phantom/live-copilot.jsonl`, `phantom.log`, and `last-crash.txt`.

Desktop structured logs are bounded and written away from the UI thread. Hosted console logging uses the built-in bounded asynchronous console queue. Stream logs are aggregate milestones only—never one event per token.

## Field contract

The canonical desktop privacy allowlist and backend structured-field allowlist live in `shared/live-copilot/prompt-contract.json`. Event fields use snake case and UTC timestamps. Dynamic URLs are logged only as route templates; IDs belong only in the designated opaque correlation fields.
