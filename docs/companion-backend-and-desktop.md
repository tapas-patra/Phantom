# Phantom Companion — Backend and Desktop Implementation Spec

**Audience:** agents and developers working in this monorepo. This is the build spec for everything that is **not** the Android app.

**Do not implement the Android app here.** That brief is [`companion-android-google-ai-studio.md`](./companion-android-google-ai-studio.md).  
**Product context:** [`companion-mobile-app.md`](./companion-mobile-app.md).

**Production API the phone already targets:** `https://phantom-ai-windows-app-backend.onrender.com`

**Status of that host today:** `GET /health` is live. `GET /api/companion/*` is **404**. Capture & Ask on the phone stays dead until this spec ships and is deployed.

**Implementation status (this repo):** Backend (`phantom-windows-app-backend/`) — DONE: migration `028_companion_pairings`, `Companion*` repositories/services/relay host, and the `/api/companion/*` routes in `Program.cs` build clean. Windows desktop (`phantom-windows-app/`) — DONE: `CompanionOrchestrator`/`CompanionRelayClient`/`CompanionCommandHost`, `HeadlessScreenCapture`, and the Companion Mode settings panel build clean. macOS desktop (`phantom-mac-app/`) — DONE: `CompanionOrchestrator`/`CompanionRelayClient`/`CompanionCommandHost`, `ScreenshotCapture.captureDisplay(id:)`/`listDisplays()`, and the Companion Mode section in `ContentView` build clean and pass `--self-check`. Android app — out of scope (see `companion-android-google-ai-studio.md`). Runtime/overlay/Speech/Keychain validation still requires native OS testing per platform.

JSON contracts in this file are frozen with the Android brief. If you change a field name, change both documents in the same change.

---

## 1. What we are adding

Three surfaces must move together:

| Surface | Repo | Job |
| --- | --- | --- |
| Hosted relay + pairing | `phantom-windows-app-backend/` | New REST + WebSocket. Authority for pairing, tickets, rooms. |
| Windows worker | `phantom-windows-app/` | Companion Mode, headless capture, relay client, command host. |
| macOS worker | `phantom-mac-app/` | Same behavior, native capture and overlay hide. |

Optional later (not v1):

| Surface | Repo | Job |
| --- | --- | --- |
| Device list | `phantom-dashboard-backend/` | Read-only companion devices. |
| Revoke UI | `phantom-website-dashboard/` | Unpair a phone from the website. |

The phone never talks to the desktop over LAN. Desktop and phone both connect **out** to the hosted backend.

```text
Android  --HTTPS/WSS-->  phantom-windows-app-backend  <--HTTPS/WSS--  Windows / macOS
                                      |
                                      + existing REST: auth, locks, /api/desktop/ai/chat
```

The relay does not generate answers. The desktop still runs the current interview pipeline.

---

## 2. What already exists — do not rebuild

Reuse these. Companion is an input path into them.

### Backend (`phantom-windows-app-backend`)

- Auth: `POST /api/desktop/auth/login|refresh|logout`, `GET /api/desktop/auth/me`
- Session gate: `DesktopSessionService.RequireSession`
- Opaque tokens: `TokenService.GenerateOpaqueToken` / `HashToken`
- One interview lock per account: `LockService` + `interview_locks` + `/api/desktop/locks/*`
- Managed/BYO chat: `POST /api/desktop/ai/chat` (desktop only)
- Schema style: append `028_...` in `Persistence/BackendSchemaMigrations.cs`
- Errors: `BackendValidationException` → `{ "error": "..." }`
- Rate limits: `auth` and `desktop-api` policies in `Program.cs`
- Browser vs native: `IsBrowserRequest` (Origin or `X-Phantom-CSRF: 1`). Desktop and phone clients must keep looking like native.

### Windows (`phantom-windows-app`)

- Hosted HTTP: `Infrastructure/Hosted/HttpHostedClientBase.cs` and `HttpHostedLockClient.cs`
- Lock acquire/heartbeat: existing hosted lock client
- Chat send / cancel / new topic: `MainWindow.xaml.cs` (`SendMessage`, `_currentRequestCancellation`, `NewTopicButton_Click` → `_conversationManager.StartNewTopic()`)
- Visible capture picker: `ScreenshotCapture.CaptureScreenshot` — **cannot** be used in Companion Mode
- Full-screen grab internals: `ScreenshotCapture.CaptureFullScreen` / `CaptureRegion` / `CopyFromScreen`
- Hide overlay: `Ctrl+Alt+`` in `MainWindow.xaml.cs`
- Settings UI: `SettingsPage.xaml` / `SettingsPage.xaml.cs`
- Persistence: `Services/SettingsManager.cs`

### macOS (`phantom-mac-app`)

- Hosted HTTP: `Sources/Phantom/BackendClient.swift`
- Lock: `RuntimeCoordinator.swift` + `BackendClient.acquireLock`
- Chat: `PhantomStore.send()`, `cancelCurrentRequest()`, new-topic via ContentView confirmation
- Area capture UI: `ScreenshotCapture.selectArea` — **cannot** be used in Companion Mode
- Silent display grab already exists inside that file: `CGDisplayCreateImage` + `encode`
- Hide: global **Command + Control + `**
- Orchestration: `PhantomStore.swift`

### Invariants that must not break

1. Only one active interview lock per account.
2. The phone never calls `/api/desktop/locks/*` or `/api/desktop/ai/chat`.
3. Wallet / usage / 15-minute blocks stay on the desktop session.
4. Dashboard backend stays read-only.

---

## 3. Build order (required)

Do not start desktop UI first. The Android app is already specified against these routes.

1. **Backend migration + pairing REST** (start, complete, list, delete).
2. **Relay ticket + in-process WebSocket** with two test roles (desktop + phone).
3. **Lock binding:** capture/ask rejected unless the paired desktop holds the live lock.
4. **Windows headless capture** (no window).
5. **Windows CompanionMode + command host + Settings pairing.**
6. **macOS same.**
7. Deploy backend to Render.
8. Only then is the Android client useful for Capture & Ask.

Local verify after step 2: `wscat` or a tiny console client. Do not wait for Android.

---

## 4. Frozen HTTP contract

Base path: `/api/companion/*`  
Auth: `Authorization: Bearer <accessToken>` from the existing desktop login session.  
JSON: camelCase.  
Errors: `400/401/409` with `{ "error": "..." }`.  
Rate limit policy: `desktop-api` (120/min/IP) for companion REST. Add a tighter in-service limit for capture commands (10/min/pairing) inside the relay.

No new `/api/companion/auth/*` in v1. Phone and desktop both keep using `/api/desktop/auth/login`. They are distinguished by `DeviceInstallId` and pairing role, not by a second token type.

### 4.1 `POST /api/companion/pairings/start`

**Caller:** desktop only.

Request:

```json
{
  "desktopDeviceLabel": "Tapas’s MacBook Pro",
  "desktopPlatform": "macos",
  "appVersion": "1.4.0"
}
```

`desktopPlatform` is `windows` or `macos`. Device id comes from the session (`session.DeviceInstallId`), not the body.

Response `200`:

```json
{
  "code": "AB7K2Q",
  "expiresAtUtc": "2026-09-11T00:10:00Z",
  "qrPayload": "phantom-companion://pair?code=AB7K2Q&relay=https://phantom-ai-windows-app-backend.onrender.com"
}
```

Code rules:

- 6 chars from `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` (no `0 O I 1`)
- 10 minutes, single use
- Starting a new code invalidates any unused code for that desktop device
- `qrPayload` host must be the public backend base the desktop was configured with (Render URL in production)

### 4.2 `POST /api/companion/pairings/complete`

**Caller:** phone only.

```json
{
  "code": "AB7K2Q",
  "companionDeviceId": "<phone installId>",
  "companionDeviceLabel": "Android Pixel 8",
  "platform": "android",
  "appVersion": "1.0.0"
}
```

Response `200`:

```json
{
  "pairingId": "pair-...",
  "desktopDeviceLabel": "Tapas’s MacBook Pro",
  "desktopPlatform": "macos",
  "createdAtUtc": "2026-09-11T00:00:00Z",
  "relayRequired": true
}
```

Errors (exact strings — Android maps them):

- `400` `Pairing code is invalid or expired.`
- `400` `Pairing code already used.`
- `409` `This desktop already has an active companion. Revoke it first.`

v1: one active pairing per desktop device. Completing a code against a desktop that already has a pairing returns 409 unless the old pairing was revoked.

### 4.3 `GET /api/companion/pairings`

Either role. Returns pairings for `session.UserId`.

```json
{
  "pairings": [
    {
      "pairingId": "pair-...",
      "desktopDeviceLabel": "Windows PC",
      "desktopPlatform": "windows",
      "companionDeviceLabel": "Android Pixel 8",
      "createdAtUtc": "2026-09-11T00:00:00Z",
      "desktopOnline": false,
      "phoneOnline": false
    }
  ]
}
```

`desktopOnline` / `phoneOnline` are true while that role has a live relay socket.

### 4.4 `DELETE /api/companion/pairings/{pairingId}`

Either role, same account. Response `{ "revoked": true }`. Must drop both sockets in the room within 5 seconds and send `relay.error` `{ "code": "pairing_revoked" }` before close.

### 4.5 `POST /api/companion/relay-ticket`

```json
{
  "pairingId": "pair-...",
  "role": "desktop"
}
```

`role` is `desktop` or `phone`.

Response:

```json
{
  "ticket": "opaque-short-lived",
  "expiresAtUtc": "2026-09-11T00:05:00Z",
  "relayUrl": "wss://phantom-ai-windows-app-backend.onrender.com/api/companion/relay"
}
```

Ticket TTL: 5 minutes. Store only the hash. Bind ticket to `userId + pairingId + role + session.DeviceInstallId`.

Admission:

- `role=desktop` → `session.DeviceInstallId` must equal the pairing’s desktop device id
- `role=phone` → `session.DeviceInstallId` must equal the pairing’s companion device id

### 4.6 `GET /api/companion/sessions/current`

Phone (or desktop) reads the last text snapshot the **desktop published**.

```json
{
  "pairingId": "pair-...",
  "desktopStatus": "ready",
  "provider": "openai",
  "model": "gpt-4.1",
  "vision": true,
  "displays": [{ "id": "0", "name": "Built-in Retina", "isDefault": true }],
  "selectedDisplayId": "0",
  "turns": [
    { "role": "user", "text": "Please analyze this screenshot.", "atUtc": "..." },
    { "role": "assistant", "text": "The question is ...", "atUtc": "..." }
  ]
}
```

If none: `404` `{ "error": "No active companion session." }` — Android already treats 404 as empty transcript.

Snapshot storage for v1: **in-memory on the relay process**, overwritten by desktop `session.snapshot` frames, max 20 turns, text only, no images. Lost on backend restart. That is acceptable.

### 4.7 Routes that must stay desktop-only

Reject `role=phone` sessions if they ever call:

- `/api/desktop/locks/*`
- `/api/desktop/ai/chat`
- `/api/companion/pairings/start`

v1 enforcement for `pairings/start`: require that the caller is not already registered as a companion device id on any pairing. Desktop start is enough if we never write the phone `installId` as a desktop id.

---

## 5. Frozen WebSocket contract

Upgrade: `GET /api/companion/relay?ticket=...`

```http
Sec-WebSocket-Protocol: phantom.companion.v1
```

Also accept a connection that omitted the subprotocol (Android retries that way). Reject missing/invalid/expired tickets with `401` before upgrade.

One live desktop socket and one live phone socket per pairing. A second socket of the same role replaces the old one (close the old with `relay.error` `replaced`).

Ping: server or client every 20s. Drop after 45s silence.

Max frame 64 KB, except `capture.completed.thumbnailJpegBase64` up to 80 KB decoded. Reject larger with `relay.error` `payload_too_large`.

Envelope:

```json
{
  "v": 1,
  "id": "01JEXAMPLULID",
  "type": "capture.ask",
  "ts": "2026-09-11T00:00:00Z",
  "pairingId": "pair-...",
  "role": "phone"
}
```

The relay:

1. Authenticates the ticket.
2. Verifies `pairingId` and `role` match the ticket.
3. Forwards phone → desktop and desktop → phone.
4. Does not persist frames.
5. Injects `relay.peer_joined` / `relay.peer_left` / `relay.error`.
6. Rate-limits `capture.full` and `capture.ask` to 10/minute/pairing and `chat.send` to 30/minute/pairing.

### 5.1 Phone → desktop (relay forwards)

| `type` | Body | Desktop must |
| --- | --- | --- |
| `session.hello` | `appVersion`, `platform` | Reply `desktop.hello` |
| `capture.full` | `displayId?`, `attachOnly` | Headless capture; attach only if `attachOnly=true` |
| `capture.ask` | `displayId?`, `prompt?` | Capture + existing send path. Default prompt: `Please analyze this screenshot.` |
| `chat.send` | `text` | Existing send, no new capture |
| `chat.cancel` | `requestId?` | Existing cancel |
| `chat.new_topic` | — | Existing new-topic / clear-context |
| `display.select` | `displayId` | Remember for later captures |

If there is no active interview lock held by **this desktop device**, desktop replies `capture.failed` / `chat.failed` with `code: "lock_missing"` and does not capture. The relay may also send `relay.error` `lock_missing` as a belt-and-suspenders check by reading `interview_locks`.

### 5.2 Desktop → phone (relay forwards)

| `type` | Body |
| --- | --- |
| `desktop.hello` | `status`, `model`, `provider`, `vision`, `displays[]`, `lockExpiresAtUtc` |
| `desktop.status` | subset |
| `session.snapshot` | same shape as `/sessions/current` minus `pairingId`; relay caches it |
| `capture.started` | `requestId`, `displayId` |
| `capture.completed` | `requestId`, `width`, `height`, `thumbnailJpegBase64?` |
| `capture.failed` | `requestId`, `code`, `message` |
| `chat.started` | `requestId`, `turnId` |
| `chat.delta` | `requestId`, `text` |
| `chat.completed` | `requestId` |
| `chat.failed` | `requestId`, `code`, `message` |
| `chat.cancelled` | `requestId` |

`status`: `offline` | `connecting` | `idle` | `ready` | `capturing` | `thinking` | `error`

`displays[]`: `{ "id": "0", "name": "Built-in Retina", "isDefault": true }`

Failure `code` values the phone already special-cases:

`desktop_offline`, `not_paired`, `lock_missing`, `vision_unsupported`, `capture_permission_missing`, `rate_limited`, `pairing_revoked`, `account_locked`

### 5.3 Relay → both

`relay.peer_joined`, `relay.peer_left`, `relay.error`, `relay.resume_ok`

v1 resume: on reconnect the phone calls `GET /api/companion/sessions/current`. You do not have to replay `chat.delta` by `eventId` in v1. If you add `eventId` later, keep it additive.

---

## 6. Backend implementation

Owner: `phantom-windows-app-backend/`  
Main entry: `Program.cs`  
Follow existing layers: `Contracts/` → `Services/` → `Persistence/` → `Domain/`.

### 6.1 Schema — add migration `028_companion_pairings`

Append to `Persistence/BackendSchemaMigrations.All`. Do not edit old SQL.

```sql
CREATE TABLE IF NOT EXISTS companion_pairings (
    pairing_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES desktop_accounts(user_id),
    desktop_device_id TEXT NOT NULL,
    desktop_device_label TEXT NOT NULL DEFAULT '',
    desktop_platform TEXT NOT NULL,
    companion_device_id TEXT NOT NULL DEFAULT '',
    companion_device_label TEXT NOT NULL DEFAULT '',
    companion_platform TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'pending',
    created_at_utc TIMESTAMPTZ NOT NULL,
    paired_at_utc TIMESTAMPTZ NULL,
    revoked_at_utc TIMESTAMPTZ NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_companion_pairings_active_desktop
    ON companion_pairings(desktop_device_id)
    WHERE revoked_at_utc IS NULL AND companion_device_id <> '';

CREATE INDEX IF NOT EXISTS idx_companion_pairings_user
    ON companion_pairings(user_id, created_at_utc DESC);

CREATE TABLE IF NOT EXISTS companion_pairing_codes (
    code_hash TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    desktop_device_id TEXT NOT NULL,
    desktop_device_label TEXT NOT NULL DEFAULT '',
    desktop_platform TEXT NOT NULL,
    app_version TEXT NOT NULL DEFAULT '',
    expires_at_utc TIMESTAMPTZ NOT NULL,
    consumed_at_utc TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS companion_relay_tickets (
    ticket_hash TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    pairing_id TEXT NOT NULL,
    role TEXT NOT NULL,
    device_id TEXT NOT NULL,
    expires_at_utc TIMESTAMPTZ NOT NULL,
    consumed_at_utc TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS companion_audit_events (
    event_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    pairing_id TEXT NOT NULL DEFAULT '',
    event_name TEXT NOT NULL,
    actor_role TEXT NOT NULL DEFAULT '',
    created_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_companion_audit_user
    ON companion_audit_events(user_id, created_at_utc DESC);
```

`status`: `pending` (code issued, unused) is **not** a row in `companion_pairings`. Codes live only in `companion_pairing_codes`. A pairing row is created on successful complete.

Audit `event_name` values: `pairing_started`, `pairing_completed`, `pairing_revoked`, `relay_connected`, `relay_disconnected`, `capture_requested`. Do **not** store prompt text, tokens, or image bytes.

No `companion_messages` table.

### 6.2 New types

Contracts (mirror Android names):

- `CompanionPairingStartRequestDto`
- `CompanionPairingStartResultDto`
- `CompanionPairingCompleteRequestDto`
- `CompanionPairingDto`
- `CompanionPairingsResponseDto`
- `CompanionRelayTicketRequestDto`
- `CompanionRelayTicketDto`
- `CompanionSessionSnapshotDto`

Domain records + repositories:

- `CompanionPairingRepository`
- `CompanionPairingCodeRepository`
- `CompanionRelayTicketRepository`
- `CompanionAuditRepository`

Services:

- `CompanionPairingService` — start / complete / list / revoke
- `CompanionRelayTicketService` — issue / consume
- `CompanionRelayHost` — in-memory rooms, sockets, snapshots, online flags

Register them in `Program.cs` the same way `LockService` is registered (singleton is fine for the in-memory relay host).

### 6.3 Pairing service rules

`Start(session, request)`:

1. `RequireSession`.
2. Reject if this `DeviceInstallId` is a `companion_device_id` on any active pairing.
3. Delete unused codes for this desktop device.
4. Create a 6-char code, store `HashToken(code)`.
5. Audit `pairing_started`.
6. Build `qrPayload` from `BackendOptions.PublicWebsiteBaseUrl` **no** — use the **backend public base**, not the website.

Add `BackendOptions.PublicApiBaseUrl` if it does not exist. Fallback chain:

1. `PHANTOM_WINDOWS_BACKEND_PUBLIC_API_BASE_URL`
2. Request `https://{Host}` when behind Render
3. Hard last resort: `https://phantom-ai-windows-app-backend.onrender.com`

Desktop QR must point at the API host, not `phantom-interview.vercel.app`.

`Complete(session, request)`:

1. Normalize code to uppercase.
2. Lookup hash. Fail if missing, expired, or consumed.
3. `session.UserId` must match the code’s user.
4. If an active pairing already exists for that desktop device → `409` exact string above.
5. Insert pairing, mark code consumed, audit `pairing_completed`.

`Revoke`:

1. Set `revoked_at_utc`.
2. `CompanionRelayHost.DropRoom(pairingId, "pairing_revoked")`.
3. Audit `pairing_revoked`.

### 6.4 WebSocket host

Use **ASP.NET Core WebSockets**, not SignalR, unless you already need a Redis backplane. This backend is one Render instance today.

```csharp
builder.Services.AddSingleton<CompanionRelayHost>();
// after UseRouting, before Map*:
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20)
});
```

Map:

```csharp
app.MapGet("/api/companion/relay", async (HttpContext http, CompanionRelayHost relay) =>
{
    if (!http.WebSockets.IsWebSocketRequest)
    {
        return Results.BadRequest(new { error = "WebSocket upgrade required." });
    }
    // consume ticket from query, accept socket, run until cancel
    await relay.AcceptAsync(http);
    return Results.Empty;
}).RequireRateLimiting("desktop-api");
```

`CompanionRelayHost` responsibilities:

- ConcurrentDictionary of rooms keyed by `pairingId`
- Each room: desktop socket, phone socket, last snapshot, last hello, rate-limit counters
- Forward frames after validating envelope `v`, `pairingId`, `role`
- On desktop `session.snapshot` / `desktop.hello`, update memory
- On revoke or process shutdown, close all sockets
- On `capture.*` from phone, increment rate limit; optionally confirm `LockRepository.FindActiveByUser` device id matches pairing desktop

Render notes:

- WebSockets work on Render web services.
- Idle connections may still die; clients already reconnect.
- A second Render instance would split rooms. Stay on **one instance** until a backplane exists. Do not enable autoscaling for this service without Redis.

### 6.5 Program.cs routes to add

Place them next to the lock routes so companion stays in the desktop-authority file.

```text
POST   /api/companion/pairings/start
POST   /api/companion/pairings/complete
GET    /api/companion/pairings
DELETE /api/companion/pairings/{pairingId}
POST   /api/companion/relay-ticket
GET    /api/companion/sessions/current
GET    /api/companion/relay          (WebSocket upgrade)
```

Each REST handler:

```csharp
var session = desktopSessions.RequireSession(httpContext.Request.Headers.Authorization.ToString());
```

Do not use cookie/CSRF browser auth for these routes.

### 6.6 Tests / verification (backend)

There is little existing test harness. Minimum before deploy:

```bash
cd phantom-windows-app-backend
dotnet build
```

Then a scripted smoke (curl + a WS client):

1. Login as a real user (desktop device metadata) → token D.
2. `POST pairings/start` with D → code.
3. Login as same user (phone device metadata) → token P.
4. `POST pairings/complete` with P + code → pairingId.
5. Ticket D + ticket P.
6. Open two websockets, hello both ways.
7. Phone sends `capture.ask`; desktop echo `capture.started` in the smoke if desktop is not connected — or use a stub desktop client.
8. `DELETE` pairing → both sockets get `pairing_revoked`.
9. Reuse code → `Pairing code already used.`
10. Expired code → `Pairing code is invalid or expired.`

`dotnet build` alone does not prove the relay.

### 6.7 Docs to update when this lands

- `phantom-windows-app-backend/CONTRACT-HANDOFF.md` — add companion APIs
- Root `AGENTS.md` — task routing line for companion
- This file’s status line

### 6.8 Out of scope for backend v1

- E2E encryption of frames
- Durable chat archive
- Multi-instance relay
- Phone-initiated interview locks
- Generating AI on the relay
- Changing `LockService` semantics

---

## 7. Windows desktop implementation

Owner: `phantom-windows-app/`  
Read `AGENTS.md` first. Do not dump this into `MainWindow.xaml.cs` if you can add a service and call 4 existing methods.

### 7.1 New files

```text
Infrastructure/Hosted/IHostedCompanionClient.cs
Infrastructure/Hosted/HttpHostedCompanionClient.cs
Infrastructure/Hosted/Contracts/Companion*.cs     # same DTOs as backend
Services/CompanionRelayClient.cs                  # OkHttp-equivalent: ClientWebSocket
Services/CompanionCommandHost.cs                  # frame → app actions
Services/HeadlessScreenCapture.cs                 # no Window
```

Settings additions stay in `SettingsPage.xaml` / `.xaml.cs` and `SettingsManager` (`CompanionEnabled`, `CompanionPairingId`).

`HostedClientFactory` must construct the companion client.

### 7.2 Headless capture (hard requirement)

`ScreenshotCapture.CaptureScreenshot` opens a dialog, activates, and steals focus. Companion Mode must never call it.

Add `HeadlessScreenCapture.CaptureDisplay(string? displayId)`:

- Enumerate `System.Windows.Forms.Screen.AllScreens`
- `displayId` `"0"` = primary, `"1"` = next, or use `DeviceName`
- `Graphics.CopyFromScreen` on that bounds
- Return `BitmapImage` / PNG bytes
- **Do not** create a `Window`, **do not** `Activate()`, **do not** show a picker
- Optional JPEG thumbnail ≤ 80 KB (long edge ~480) for `capture.completed`

Wire `CaptureScreenshot()` in `MainWindow` so the toolbar still uses the picker. Companion host calls only the headless helper.

### 7.3 Command host

`CompanionCommandHost` runs on the UI dispatcher and calls existing behavior:

| Frame | Existing hook |
| --- | --- |
| `capture.full` attachOnly | headless capture → `_attachedScreenshots.Add` |
| `capture.ask` | headless capture → attach → `SendMessage()` with default or given prompt |
| `chat.send` | set input text → `SendMessage(captureQuestion: false)` |
| `chat.cancel` | `_currentRequestCancellation.Cancel()` |
| `chat.new_topic` | `_conversationManager.StartNewTopic()` |
| `display.select` | store selected screen id |

While generating, the host must push `chat.delta` for each streamed chunk `MainWindow` already appends. The cleanest way: a small callback/event on the conversation stream (`OnAssistantDelta`) rather than scraping the RichTextBox. If you must hook `MainWindow` first, do it once and leave a TODO to lift the stream event.

After each completed or cancelled turn, send `session.snapshot` (last 20 text turns, no images).

`desktop.hello` / `desktop.status` fields:

- `provider` / `model` from current settings / registry
- `vision` from `AIModelRegistry` vision flag (same check that hides the screenshot button)
- `displays` from `Screen.AllScreens`
- `lockExpiresAtUtc` from the current hosted lock
- `status` mapping: hidden+connected+not generating → `ready`; generating → `thinking`; capturing → `capturing`

### 7.4 Companion Mode

New setting: `CompanionModeEnabled`.

When on:

1. Ensure hosted lock is acquired or same-device resume is valid. If not, stay `idle` and tell the phone `lock_missing` on capture/ask. Do not auto-start a new interview from Companion Mode.
2. `POST /api/companion/relay-ticket` with `role=desktop`.
3. Open `ClientWebSocket` to `relayUrl`.
4. Hide the overlay the same path as `Ctrl+Alt+``.
5. Keep lock heartbeat running (already does).
6. Do not open Settings, message boxes, or the capture picker.
7. Surface errors only through relay frames.

When off: close the socket, overlay returns to normal hide/show.

Do not start Companion Mode automatically on boot in v1. User enables it in Settings after pairing.

### 7.5 Settings → Companion section

Add a panel (not a new window that flashes in Task View):

1. Toggle: Enable Companion Mode
2. Button: Show pairing code — calls `pairings/start`, shows code + QR (`qrPayload` as QR)
3. Current pairing label + Unpair (`DELETE`)
4. Status: Relaying / Waiting for phone / Not paired
5. Note: “Grant nothing extra on Windows beyond what Phantom already uses. Pair before the session.”

QR: use a small local QR library or render via a well-known package. Do not open a browser.

Pairing UI is allowed to be visible. That is setup, not Companion Mode.

### 7.6 Hosted client methods

Extend the hosted stack; do not invent a second HttpClient stack.

```text
StartPairingAsync(...)
ListPairingsAsync(...)
RevokePairingAsync(pairingId)
CreateRelayTicketAsync(pairingId, "desktop")
```

Reuse `HttpHostedClientBase` JSON + bearer + correlation headers.

### 7.7 Windows verification

`dotnet build SecureOverlay.sln -c Debug` is the static bar. Runtime must be checked on Windows:

1. Sign in, start/resume interview (so a lock exists).
2. Settings → Show pairing code.
3. Complete pairing from a second HTTP client (or Android).
4. Enable Companion Mode → overlay hides, no new window.
5. From a stub phone socket, send `capture.ask`.
6. Confirm no focus steal (fullscreen Notepad / browser stays focused).
7. Confirm tokens stream back.
8. Cancel and new-topic from the stub.
9. Unpair → socket dies.

A Mac-only agent cannot claim this passed.

---

## 8. macOS desktop implementation

Owner: `phantom-mac-app/`  
Read `README.md` and `PhantomStore.swift`.

### 8.1 New files

```text
Sources/Phantom/CompanionRelayClient.swift
Sources/Phantom/CompanionCommandHost.swift
```

Capture helper stays in `ScreenshotCapture.swift`: add `captureDisplay(id:)` that uses `CGDisplayCreateImage` + `encode` and **does not** present `AreaSelectionController`.

`BackendClient.swift` gets pairing + ticket methods next to `acquireLock`.

Settings: add a Companion block in the existing settings surface (in-panel, no extra menu window — the README already forbids brief visible menus).

### 8.2 Command host

Map to `PhantomStore`:

| Frame | Store API |
| --- | --- |
| `capture.full` | `captureDisplay` → `attachedScreenshots` |
| `capture.ask` | attach + `draft = prompt` + `send()` |
| `chat.send` | `draft = text` + `send()` |
| `chat.cancel` | `cancelCurrentRequest()` |
| `chat.new_topic` | existing clear / new-topic path |
| stream | hook the same stream callback `send()` already consumes |

Hide overlay with the existing hide shortcut path. Companion Mode must not `NSApp.activate(ignoringOtherApps: true)` — that is what `AreaSelectionController.present()` does today and is why area capture is forbidden in this mode.

Screen Recording permission: if `CGPreflightScreenCaptureAccess()` is false, send `capture.failed` `capture_permission_missing`. Do **not** call `CGRequestScreenCaptureAccess()` from Companion Mode (that is a system prompt). The Settings Companion panel can offer “Request permission” and “Open System Settings” during setup.

### 8.3 Displays

`NSScreen.screens` → `id` = `NSScreenNumber` as string, `name` = localized name, `isDefault` = main.

### 8.4 macOS verification

```bash
swift build --disable-sandbox --package-path phantom-mac-app
./phantom-mac-app/.build/debug/Phantom --self-check
```

Self-check does **not** prove overlay, ScreenCaptureKit, or Keychain. Runtime matrix on a Mac:

1. Grant Screen Recording in Settings **before** Companion Mode.
2. Pair, hide overlay, `capture.ask` from a stub phone.
3. Confirm no Space switch and no activation of Phantom.
4. Zoom/Meet full screen stays frontmost.
5. Unpair.

Do not claim Windows parity from a Mac run.

---

## 9. Shared desktop behavior checklist

Both clients must:

- Use the existing access token; no second login for relay
- Fetch a new relay ticket on every connect
- Reconnect 1s → 15s cap
- Heartbeat the interview lock as they do today
- Refuse capture/ask without a lock (`lock_missing`)
- Refuse capture/ask when the model is not vision-capable (`vision_unsupported`)
- Default prompt: `Please analyze this screenshot.`
- Cap attachments at 3 (existing `DesktopAiChatRequestDto.MaxAttachedImages`)
- Never open picker / permission / error dialogs while Companion Mode is on
- Publish `session.snapshot` after each turn
- Close relay on sign-out, lock release, and unpair

Both clients must not:

- Call phone-only routes (`pairings/complete`)
- Stream full-resolution PNGs through the relay
- Change billing
- Auto-launch Companion Mode after a crash in v1 (user re-enables)

---

## 10. Dashboard and website (not v1)

After pairing works:

- Project companion devices into `dashboard_device_inventory` **or** a new `dashboard_companion_devices` table via the existing projection replicator
- Website device page: show “Android Pixel 8 (Companion)” and Revoke → `DELETE /api/companion/pairings/{id}` on the authority backend
- Dashboard backend remains read-only

Do not block v1 on this. Desktop Settings Unpair is enough.

---

## 11. Deploy

### Backend to Render

Service already at `https://phantom-ai-windows-app-backend.onrender.com`.

After merge:

1. Deploy `phantom-windows-app-backend`.
2. Confirm migration `028_companion_pairings` applied (`__schema_migrations`).
3. `GET /api/companion/pairings` with a bearer token returns `200 { "pairings": [] }`, **not** 404.
4. `GET /api/companion/relay` without a ticket returns 401, not 404.
5. Keep instance count at 1.

Optional new env:

```text
PHANTOM_WINDOWS_BACKEND_PUBLIC_API_BASE_URL=https://phantom-ai-windows-app-backend.onrender.com
```

Used only to build `qrPayload`.

### Desktop releases

Windows and macOS need a release that contains Companion Mode before the Android app can do more than login. Ship backend first; old desktops ignore unknown nothing because they simply lack the client. New desktops talking to an old backend will see 404 on `pairings/start` — show “Update / backend not ready” in Settings.

### Android

No backend change is required on the phone if this contract is implemented as written. After Render deploy, the Android capability probe flips from 404 to 200.

---

## 12. Suggested file-level task list

Use this as the implementation ticket list.

### Backend

- [ ] `028_companion_pairings` migration
- [ ] Domain records + 4 repositories
- [ ] `CompanionPairingService` + `CompanionRelayTicketService`
- [ ] `CompanionRelayHost` (WebSockets)
- [ ] DTOs in `Contracts/`
- [ ] Routes in `Program.cs`
- [ ] `BackendOptions.PublicApiBaseUrl`
- [ ] DI registration
- [ ] `CONTRACT-HANDOFF.md` update
- [ ] Smoke script for pairing + WS
- [ ] `dotnet build`

### Windows

- [ ] Companion DTOs + `HttpHostedCompanionClient`
- [ ] `HeadlessScreenCapture`
- [ ] `CompanionRelayClient` + `CompanionCommandHost`
- [ ] Settings Companion panel + QR/code
- [ ] Companion Mode hide + no-dialog rule
- [ ] Stream deltas → `chat.delta`
- [ ] Snapshot publisher
- [ ] Settings persistence
- [ ] `dotnet build SecureOverlay.sln -c Debug`

### macOS

- [ ] `BackendClient` companion methods
- [ ] `ScreenshotCapture.captureDisplay`
- [ ] `CompanionRelayClient` + `CompanionCommandHost`
- [ ] Settings Companion panel
- [ ] Companion Mode hide + no `activate`
- [ ] Permission-missing mapping
- [ ] `swift build` + `--self-check`

---

## 13. Done when

Backend is done when a desktop token and a phone token for the same account can pair, open a room, forward `capture.ask`, revoke, and the Android probe stops returning 404 on Render.

Windows is done when Companion Mode can execute `capture.ask` without showing a window or taking focus, and tokens appear on the phone socket.

macOS is done when the same is true without activating Phantom or presenting the area selector.

The Android app is **not** part of this ticket. If you need to test without Android, use two `websocat` clients.

---

## 14. Non-goals

- Beating OS focus policies, lockdown browsers, or third-party monitoring
- Hiding the desktop process
- LAN / Bluetooth fallback
- Remote desktop video
- Remote crop lasso
- Phone-side generation
- Multi-phone pairings
- E2E relay encryption (v1.1+)
