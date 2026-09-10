# Phantom Companion — Android Production Brief for Google AI Studio

**Audience:** a Google AI Studio / Gemini Android agent that must ship a production-ready Kotlin app and hand it back complete.

**Related product spec:** [`companion-mobile-app.md`](./companion-mobile-app.md)  
**Backend + desktop build spec (this monorepo):** [`companion-backend-and-desktop.md`](./companion-backend-and-desktop.md)

**Do not invent APIs.** Use only the contracts in this file. If a companion route is missing on the server, degrade in-app. Do not fake capture results.

---

## 0. How to use this file in Google AI Studio

Paste this entire document as the build prompt. Then add only this instruction:

> Build the complete Android app described below. Use Kotlin, Jetpack Compose, and the exact backend contracts. Do not skip error states, token storage, reconnect, or the capability probe. When you are done, hand over a buildable project with a README that lists package name, how to assemble a release, and which live endpoints were verified.

The agent must return:

1. A full Gradle Android project (not a gist of one Activity).
2. Working login against the production backend.
3. Pairing + session UI wired to the companion contract.
4. A `README.md` and `HANDOVER.md` inside the project.
5. Debug and release build types.
6. No placeholder buttons that do nothing.

---

## 1. Product

**App name:** Phantom Companion  
**Application id:** `com.phantom.companion`  
**Namespace:** `com.phantom.companion`  
**Version:** `1.0.0` / versionCode `1`

Phantom Companion is the Android remote control for an already-running Phantom Windows or macOS desktop client. The phone does **not** generate answers and does **not** capture the computer screen. It sends commands through Phantom’s hosted backend. The desktop captures, builds the prompt, and generates. The phone shows the streamed answer.

Connection is **hosted relay only**. Never scan the LAN, never ask for a desktop IP, never use Bluetooth, Nearby, or Wi-Fi Direct.

This app is for hands-off desktop use: the computer’s foreground app stays untouched. Do not claim the app is invisible to operating systems or monitoring tools.

---

## 2. Production backend (locked)

```text
REST base:  https://phantom-ai-windows-app-backend.onrender.com
WS  base:   wss://phantom-ai-windows-app-backend.onrender.com
Health:     GET /health
```

Verified live on 2026-09-10:

```json
{"status":"live","service":"phantom-windows-app-backend","utc":"2026-09-10T22:43:21.4003702Z"}
```

Also verified: `GET /api/companion/pairings` returns **404**. Companion routes are specified below and **must still be implemented in the Android client**. The app probes them and shows a clear backend-not-ready state until they exist.

Put the base URL in `BuildConfig`:

| Build type | `PHANTOM_API_BASE_URL` |
| --- | --- |
| `debug` | `https://phantom-ai-windows-app-backend.onrender.com` |
| `release` | `https://phantom-ai-windows-app-backend.onrender.com` |

Allow a debug override via `local.properties` key `phantom.api.baseUrl` for future local backends. Default is always the Render URL above.

Render free/starter services **cold-start**. First request can take up to 60 seconds. Timeouts:

- Health / login / refresh / startup-check: connect 20s, read 60s
- Companion REST: connect 20s, read 30s
- WebSocket: connect 20s, ping 20s, stale 45s

On a slow first call, show “Waking Phantom…” not a generic failure.

---

## 3. Locked tech stack

The agent must use this stack. Do not switch to Flutter, React Native, Java-only, or a WebView shell.

| Piece | Choice |
| --- | --- |
| Language | Kotlin 2.x |
| UI | Jetpack Compose + Material 3 |
| Min SDK | 26 |
| Target / compile SDK | 35 |
| Architecture | single-module `app` + clear packages |
| HTTP | OkHttp 4 + Retrofit 2 + Kotlinx Serialization |
| WebSocket | OkHttp `WebSocket` |
| DI | Hilt |
| Navigation | Navigation Compose |
| Images / QR | CameraX + ML Kit barcode scanning **or** ZXing embedded. Prefer ML Kit. Manual 6-character code entry is required either way. |
| Secure storage | EncryptedSharedPreferences + Android Keystore |
| Coroutines | Yes |
| Logging | Timber in debug only. Never log tokens, passwords, pairing codes, or answer text in release. |

**Forbidden:**

- WebView for API calls
- Sending `Origin` or `X-Phantom-CSRF: 1` (that makes the backend treat the client as a browser and **strip access/refresh tokens** from the login body)
- Storing tokens in plain `SharedPreferences`, DataStore Preferences, or files
- Calling `/api/desktop/ai/chat` from the phone
- Acquiring a desktop interview lock from the phone
- Admin APIs
- Hard-coded user credentials
- Mock answers presented as real desktop output

---

## 4. Critical auth rule (read twice)

Desktop/native clients get tokens in the JSON body. Browser clients do not.

The backend treats a request as a browser if **either** is true:

- `Origin` header is present
- `X-Phantom-CSRF` is `1`

OkHttp must not add those headers. Login/refresh responses for this app must contain `accessToken` and `refreshToken`. If they are missing, the request was misclassified — fix the headers, do not work around it.

JSON is camelCase. Dates are ISO-8601 and may arrive with or without `Z` / fractional seconds. Parse all of:

- `2026-09-10T22:43:21.4003702Z`
- `2026-09-10T22:43:21.4003702`
- `2026-09-10T22:43:21Z`
- `2026-09-10T22:43:21`

---

## 5. Device identity (required on every auth call)

Generate once per install and persist forever (until app data is cleared).

```text
installId              = UUIDv4, persisted
deviceSecret           = 32 random bytes, Keystore-backed
deviceLabel            = "Android " + Build.MODEL   e.g. "Android Pixel 8"
deviceFingerprintHash  = sha256_hex(installId + "|" + deviceLabel + "|" + base64(deviceSecret))
secretFingerprintHint  = first 12 chars of sha256_hex(base64(deviceSecret))
appVersion             = BuildConfig.VERSION_NAME
```

SHA-256 hex is lowercase. This matches the macOS client pattern.

Refresh **must** send the same `installId` and `deviceFingerprintHash` as login. A mismatch returns `Refresh token device mismatch.` and the user must sign in again.

`installId` should be 8–128 chars, ASCII letters/digits/`-`/`_`. A UUID is valid.

---

## 6. Live APIs the app must wire now

All paths are relative to the REST base. All authenticated calls send:

```http
Authorization: Bearer <accessToken>
Content-Type: application/json
Accept: application/json
X-Phantom-Correlation-Id: <ulid-or-uuid>
X-Phantom-Operation-Id: <ulid-or-uuid>
```

Generate a new correlation id per request. Do not send CSRF or Origin.

Validation errors are:

```http
HTTP 400
{"error":"Invalid email or password."}
```

Rate limit:

```http
HTTP 429
Retry-After: 5
{"error":"Too many requests. Please wait a few seconds and try again."}
```

Auth login is limited to **20 requests / IP / minute**. Other auth routes **30 / IP / minute**. Desktop-api routes **120 / IP / minute**.

Unauthorized refresh/me: HTTP 401, empty or unauthenticated. Clear tokens and return to Sign In.

### 6.1 `GET /health`

No auth.

```json
{"status":"live","service":"phantom-windows-app-backend","utc":"2026-09-10T22:43:21.4003702Z"}
```

Call this on a 60s timeout when the first authenticated call fails with a timeout. If health becomes live, retry the original call once.

### 6.2 `POST /api/desktop/auth/login`

No bearer token.

```json
{
  "email": "user@example.com",
  "password": "secret",
  "appVersion": "1.0.0",
  "installId": "8f14e45f-ea5d-4c0d-9e0a-1a2b3c4d5e6f",
  "deviceLabel": "Android Pixel 8",
  "deviceFingerprintHash": "64-char-hex",
  "secretFingerprintHint": "12-char-hex"
}
```

Success `200`:

```json
{
  "userId": "user-...",
  "email": "user@example.com",
  "accessToken": "opaque",
  "refreshToken": "opaque",
  "authMethod": "password",
  "deviceInstallId": "8f14e45f-ea5d-4c0d-9e0a-1a2b3c4d5e6f",
  "deviceFingerprintHash": "64-char-hex",
  "authenticatedAtUtc": "2026-09-10T22:43:21.4003702Z",
  "expiresAtUtc": "2026-09-11T10:43:21.4003702Z",
  "isAuthenticated": true
}
```

Default access-token lifetime is **12 hours**. Refresh before expiry (at 80% of remaining TTL, and on 401).

Known `400.error` strings to map in UI:

| Server error | UI |
| --- | --- |
| `Email is required.` | Inline on email |
| `Password is required.` | Inline on password |
| `Invalid email or password.` | Banner, do not reveal which field |
| `Please verify your email before signing in.` | Dedicated email-verification state with link hint |
| `This account is temporarily locked...` | Account locked state |

Tokens are opaque strings, not JWTs. Do not try to decode them.

### 6.3 `POST /api/desktop/auth/refresh`

```json
{
  "refreshToken": "opaque",
  "installId": "same-as-login",
  "deviceFingerprintHash": "same-as-login"
}
```

Success: same `AuthSession` shape as login, new tokens. Persist the new pair and drop the old refresh token.

Failure: 401. Sign out locally.

### 6.4 `POST /api/desktop/auth/logout`

```json
{ "refreshToken": "opaque" }
```

Success: `{ "revoked": true }`. Always clear local session even if the network fails.

### 6.5 `GET /api/desktop/auth/me`

Bearer required. Returns session metadata **without** tokens:

```json
{
  "userId": "...",
  "email": "...",
  "authMethod": "password",
  "deviceInstallId": "...",
  "deviceFingerprintHash": "...",
  "authenticatedAtUtc": "...",
  "expiresAtUtc": "...",
  "isAuthenticated": true
}
```

Use after cold start if a session exists, then run startup-check.

### 6.6 `POST /api/desktop/account/startup-check/session`

Bearer required. Body must be the **full AuthSession object** and must match the bearer token exactly (`userId`, `email`, `deviceInstallId`, `deviceFingerprintHash`). Sending a partial body fails with:

`Startup session payload does not match the authenticated desktop session.`

Success (fields the phone must read):

```json
{
  "userId": "...",
  "email": "...",
  "emailVerified": true,
  "accessTier": "free",
  "phoneVerified": true,
  "wallet": {
    "proAvailableCredits": 0,
    "premiumAvailableCredits": 0,
    "premiumNegativeCredits": 0
  },
  "leaseExpiresAtUtc": null,
  "hasResumableLockedSession": false,
  "lastLockTokenHash": "",
  "lastLockedSessionId": "",
  "offlineModeEnabled": false,
  "canUseDesktopPowerFeatures": true,
  "lastValidatedAtUtc": "2026-09-10T22:43:21Z",
  "hostedKnowledgeBase": {
    "knowledgeBaseId": "",
    "name": "",
    "status": "not_created",
    "canUseInInterview": false,
    "blockedReason": ""
  },
  "source": "session_check"
}
```

`accessTier` is `free` | `pro` | `premium` (treat unknown as display-only).

Startup gate for the phone:

- `emailVerified == false` → block the app, tell user to verify on the Phantom website
- `hasResumableLockedSession == true` → desktop interview is live or resumable; companion may connect
- credits `<= 0` and no resumable lock → allow shell, disable Capture & Ask, show “Add credits on the Phantom website”
- `premiumNegativeCredits > 0` → same restriction
- otherwise allow pairing / session

The phone never starts an interview. It only joins a desktop that already holds the lock.

### 6.7 `POST /api/desktop/auth/forgot-password`

```json
{ "email": "user@example.com" }
```

Success always looks like:

```json
{ "message": "If that account exists, a password reset link has been sent." }
```

Show that message. Reset completion happens on the website, not in this app.

### 6.8 APIs the phone must not call

- `/api/desktop/auth/register` — registration stays on the website
- `/api/desktop/ai/chat` and any managed-AI generation route
- `/api/desktop/locks/*`
- `/api/desktop/usage/reconcile`
- `/api/admin/*`
- `/api/internal/*`

---

## 7. Companion APIs (implement the client now)

These routes are **not on Render yet** (404 as of this brief). Implement every method, then run a capability probe.

Probe on each launch after login, in this order:

1. `GET /health` — backend awake
2. `GET /api/companion/pairings` with bearer — if 404, set `companionApiReady = false`
3. If 200, set `companionApiReady = true` and cache the list

When `companionApiReady == false`:

- User can still sign in, see account, wallet, and “Desktop companion service is not enabled on this backend yet.”
- Pair / Capture buttons are disabled
- Do not crash, do not mock a paired desktop

When the routes start returning 200/401 instead of 404, the same build works without a store update if the JSON matches this contract.

### 7.1 `POST /api/companion/pairings/start`

**Caller:** desktop only. The Android app does **not** call this. The desktop shows the QR / 6-character code.

### 7.2 `POST /api/companion/pairings/complete`

**Caller:** phone. Bearer = phone access token.

```json
{
  "code": "AB7K2Q",
  "companionDeviceId": "<installId>",
  "companionDeviceLabel": "Android Pixel 8",
  "platform": "android",
  "appVersion": "1.0.0"
}
```

Success `200`:

```json
{
  "pairingId": "pair-...",
  "desktopDeviceLabel": "Tapas’s MacBook Pro",
  "desktopPlatform": "macos",
  "createdAtUtc": "2026-09-11T00:00:00Z",
  "relayRequired": true
}
```

Errors:

- `400` `{"error":"Pairing code is invalid or expired."}`
- `400` `{"error":"Pairing code already used."}`
- `409` `{"error":"This desktop already has an active companion. Revoke it first."}`

Code format: 6 characters, `A–Z2–9` (no `0 O I 1`). Normalize to uppercase. Expiry on server: 10 minutes, single use.

### 7.3 `GET /api/companion/pairings`

Bearer. Returns the account’s pairings.

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

v1: at most one pairing. If a list comes back, use the first and allow Unpair.

### 7.4 `DELETE /api/companion/pairings/{pairingId}`

Bearer. Success `{ "revoked": true }`. Close any open WebSocket and return to Pair.

### 7.5 `POST /api/companion/relay-ticket`

Bearer.

```json
{
  "pairingId": "pair-...",
  "role": "phone"
}
```

Success:

```json
{
  "ticket": "opaque-short-lived",
  "expiresAtUtc": "2026-09-11T00:05:00Z",
  "relayUrl": "wss://phantom-ai-windows-app-backend.onrender.com/api/companion/relay"
}
```

Ticket TTL is 5 minutes. Fetch a new ticket on every socket connect. Do not persist tickets.

If `relayUrl` is omitted, default to:

`wss://phantom-ai-windows-app-backend.onrender.com/api/companion/relay?ticket=<url-encoded-ticket>`

### 7.6 `GET /api/companion/sessions/current`

Bearer. Text-only resume after process death.

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

If 404, start with an empty transcript.

---

## 8. WebSocket protocol (`phantom.companion.v1`)

Connect only after a relay ticket.

```text
wss://phantom-ai-windows-app-backend.onrender.com/api/companion/relay?ticket=<ticket>
```

Request headers:

```http
Sec-WebSocket-Protocol: phantom.companion.v1
Authorization: Bearer <accessToken>
```

If the server rejects the subprotocol, retry once without `Sec-WebSocket-Protocol` but keep the ticket query. Do not fall back to ws://.

All frames are JSON text. Max 64 KB except `capture.completed.thumbnailJpegBase64` which may be up to 80 KB decoded.

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

Use ULID or UUID for `id`. Phone always sends `role: "phone"`.

### 8.1 Phone → desktop

**`session.hello`**

```json
{ "appVersion": "1.0.0", "platform": "android" }
```

**`capture.full`**

```json
{ "displayId": "0", "attachOnly": true }
```

**`capture.ask`**

```json
{ "displayId": "0", "prompt": "Please analyze this screenshot." }
```

Omit `prompt` to use that default. This is the primary v1 action.

**`chat.send`**

```json
{ "text": "Give the answer in 4 bullets." }
```

**`chat.cancel`**

```json
{ "requestId": "optional-in-flight-id" }
```

**`chat.new_topic`** — empty body.

**`display.select`**

```json
{ "displayId": "1" }
```

### 8.2 Desktop / relay → phone

Handle every type. Ignore unknown types without crashing (forward-compat).

| `type` | Required fields | UI |
| --- | --- | --- |
| `desktop.hello` | `status`, `model`, `provider`, `vision`, `displays[]`, `lockExpiresAtUtc` | Status pill + model line |
| `desktop.status` | subset of hello | Update pill |
| `capture.started` | `requestId`, `displayId` | “Capturing…” |
| `capture.completed` | `requestId`, `width`, `height`, `thumbnailJpegBase64?` | Optional thumb chip |
| `capture.failed` | `requestId`, `code`, `message` | Error banner |
| `chat.started` | `requestId`, `turnId` | Lock composer, show thinking |
| `chat.delta` | `requestId`, `text` | Append to current assistant bubble |
| `chat.completed` | `requestId` | Unlock composer |
| `chat.failed` | `requestId`, `code`, `message` | Error on that turn |
| `chat.cancelled` | `requestId` | Mark cancelled |
| `relay.peer_joined` | `role` | If `desktop`, set ready |
| `relay.peer_left` | `role` | If `desktop`, “Waiting for desktop” |
| `relay.error` | `code`, `message` | Banner; if `pairing_revoked`, unpair |
| `relay.resume_ok` | — | Hide reconnect banner |

`status` values: `offline` | `connecting` | `idle` | `ready` | `capturing` | `thinking` | `error`

`code` values the UI must special-case:

- `desktop_offline`
- `not_paired`
- `lock_missing`
- `vision_unsupported`
- `capture_permission_missing`
- `rate_limited`
- `pairing_revoked`
- `account_locked`

### 8.3 Socket lifecycle

1. Fetch relay ticket.
2. Open socket.
3. Send `session.hello`.
4. Wait for `desktop.hello` or `relay.peer_left`.
5. App ping: send a protocol-level OkHttp ping every 20s. If no pong in 45s, reconnect.
6. Reconnect with backoff 1s, 2s, 4s, 8s, 15s, cap 15s.
7. On each reconnect, new ticket + `GET /api/companion/sessions/current`.
8. Keep the screen on while `status == thinking` (`FLAG_KEEP_SCREEN_ON`).
9. On `pairing_revoked` or `DELETE` pairing, close and do not reconnect.

If the desktop never joins: stay on Session with a waiting state. Do not auto-unpair.

---

## 9. App architecture

```text
com.phantom.companion
  PhantomApp
  di/
  data/
    local/     DeviceIdentityStore, SessionStore
    remote/    AuthApi, CompanionApi, RelayClient
    repo/      AuthRepository, PairingRepository, SessionRepository
  domain/
    model/     AuthSession, StartupSnapshot, Pairing, RelayFrame, ChatTurn
  ui/
    theme/
    nav/
    signin/
    pair/
    session/
    account/
  MainActivity
```

Unidirectional data: Repository → ViewModel (`StateFlow`) → Compose.

One `PhantomApi` Retrofit interface for REST. One `RelayClient` for the socket. Screens never open sockets.

---

## 10. Screens and behavior

Dark theme only. Full-screen, edge-to-edge, 24dp page padding.

### 10.1 Sign In

- Wordmark: **Phantom** with small caps subtitle **Companion**
- Email, password, Sign in
- Forgot password → dialog collecting email → call forgot-password → show server message
- No registration form. Footer: “Create an account on the Phantom website.”
- Disable the button while in flight
- After success: persist session, startup-check, then Pair or Session

### 10.2 Account gate (full screen, not a toast)

Show when startup-check blocks:

- Email not verified
- Account locked
- Backend waking / unreachable (with Retry)

### 10.3 Pair

If no pairing:

- Camera preview + “Scan desktop QR”
- “Or enter code” 6-box input, auto-uppercase
- Hint: “Open Phantom on your computer → Settings → Companion”
- Permission-denied state with a button to system settings

If paired: skip to Session.

### 10.4 Session (the product)

Top:

- Status pill: Desktop offline / Connecting / Ready / Capturing / Thinking / Error
- Email (truncated) · access tier · model name when known
- Overflow: Account

Center:

- Transcript. User bubbles right, assistant left. Stream deltas into the last assistant bubble.
- Empty state: “Desktop is ready. Tap Capture & Ask.”
- Waiting-for-desktop state if socket up but no `desktop.hello`
- Companion-API-missing state if probe 404

Bottom:

1. Primary: **Capture & Ask** — full width, prominent
2. Row: **Capture only** | **Stop**
3. Composer + send (follow-up, no new capture)
4. Text button: **New topic** (confirm dialog)

Rules:

- All action buttons disabled when desktop is not `ready` or `thinking` (Stop stays enabled while thinking)
- Capture & Ask disabled when `vision == false`; show “This model cannot read screens”
- Capture & Ask disabled when `lock_missing`; show “Start the interview on the desktop first”
- Never show a fake answer
- Optional thumbnail under the last user turn if `capture.completed` included one. Do not persist it to disk.

### 10.5 Account

- Email, tier, wallet credits (Pro / Premium / debt)
- Paired desktop label
- Companion API status (ready / not deployed)
- Unpair
- Sign out
- Version + backend host

---

## 11. Design system

Match the Phantom website night theme. Do not use default purple Material.

| Token | Value |
| --- | --- |
| Background | `#08111E` |
| Surface | `#132235` |
| Surface high | `#0F1B2A` |
| Line | `#263950` |
| Text | `#EDF3FB` |
| Muted | `#9BADC0` |
| Primary | `#315CF5` |
| Primary dark | `#2448C9` |
| Accent | `#A995FF` |
| Success | `#72D9B4` |
| Warning | `#D8922D` |
| Danger | `#CE445C` |
| Radius large | 22.dp |
| Radius medium | 14.dp |
| Radius small | 10.dp |

Typography: `sans-serif` is fine (DM Sans is not required on device). Status labels and pairing codes use `monospace`.

Primary button: filled `#315CF5`, white label, 52.dp height, 14.dp corners.  
Status pill: 8.dp pill, muted surface, success/warning/danger dot.

No marketing illustrations. No Lottie splash longer than 300ms. No onboarding carousel.

---

## 12. Security and privacy

- Tokens only in EncryptedSharedPreferences
- Device secret in Android Keystore (or EncryptedSharedPreferences if Keystore API is wrapped by Tink via EncryptedSharedPreferences)
- Screenshot thumbnails only in memory
- Clear transcript on Sign out and Unpair
- Certificate validation on; no trust-all debug bypass in release
- `network_security_config`: HTTPS only to `phantom-ai-windows-app-backend.onrender.com` (plus optional debug localhost)
- Backup: `android:allowBackup="false"` and `android:dataExtractionRules` deny tokens
- FLAG_SECURE is **not** required on Sign In; do **set** `FLAG_SECURE` on Session so recents/screenshots do not capture answers
- Microphone permission only if voice follow-up is implemented (v1.1). v1 is text-only plus capture commands
- Camera permission only for QR

v1 does **not** need E2E encryption of relay frames. TLS to Render is enough for this build. Do not invent a crypto protocol.

---

## 13. Permissions (`AndroidManifest.xml`)

```xml
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
<uses-permission android:name="android.permission.CAMERA" />
<uses-permission android:name="android.permission.VIBRATE" />
```

Do not add `RECORD_AUDIO` in v1.

Deep link:

```text
phantom-companion://pair?code=AB7K2Q&relay=https://phantom-ai-windows-app-backend.onrender.com
```

If the code query is present, jump to Pair with the code filled. Ignore `relay` unless it exactly equals the BuildConfig base URL; never switch hosts from a QR.

---

## 14. Error and copy catalog

Use these strings. Do not get creative.

- `Waking Phantom…` — cold start
- `Invalid email or password.`
- `Verify your email on the Phantom website, then try again.`
- `Too many attempts. Wait a few seconds.`
- `Desktop companion service is not enabled on this backend yet.`
- `Waiting for desktop…`
- `Start the interview in Phantom on your computer first.`
- `This model cannot read screens.`
- `Capture failed. Check desktop screen-recording permission.`
- `Pairing code is invalid or expired.`
- `Signed out.`
- `Connection lost. Reconnecting…`

---

## 15. What production-ready means for handover

The agent is not done until all of this is true:

1. `./gradlew :app:assembleDebug` succeeds.
2. `./gradlew :app:assembleRelease` succeeds with minify enabled and a working ProGuard/R8 keep set for Kotlinx Serialization + Retrofit.
3. Cold start with no session → Sign In.
4. Login against the production URL with a real Phantom account succeeds and stores tokens (the agent cannot have the user’s password; they must leave a test checklist).
5. Failed login shows the server `error` string.
6. 401 on refresh returns to Sign In.
7. Startup-check is called with the **full** session body.
8. Companion 404 shows the not-enabled state instead of crashing.
9. When companion APIs exist, Pair → Session → Capture & Ask → stream works without a code change if JSON matches this file.
10. Process death on Session restores tokens and reconnects.
11. Unpair and Sign out both work.
12. No secrets in source. No `.idea` user tokens.
13. `HANDOVER.md` lists:
    - package / version
    - backend URL used
    - endpoints verified (method, status)
    - companion probe result
    - how to generate a signed AAB
    - known gaps

---

## 16. Suggested implementation order for the agent

1. Gradle app + theme + navigation graph.
2. Device identity + EncryptedSharedPreferences.
3. Retrofit `AuthApi` + login/refresh/logout/me/startup-check/forgot-password.
4. Sign In screen + session restore.
5. Capability probe + Account screen.
6. Companion Retrofit + Pair screen (works against 404 as disabled).
7. `RelayClient` + Session screen state machine.
8. Deep link.
9. Release minify + handover docs.
10. Only then: optional voice follow-up. Do not start here.

---

## 17. Kotlin models (copy these names)

```kotlin
@Serializable
data class ErrorBody(val error: String? = null)

@Serializable
data class AuthSession(
    val userId: String,
    val email: String,
    val accessToken: String = "",
    val refreshToken: String = "",
    val authMethod: String = "",
    val deviceInstallId: String,
    val deviceFingerprintHash: String,
    val authenticatedAtUtc: String? = null,
    val expiresAtUtc: String? = null,
    val isAuthenticated: Boolean = false
)

@Serializable
data class LoginRequest(
    val email: String,
    val password: String,
    val appVersion: String,
    val installId: String,
    val deviceLabel: String,
    val deviceFingerprintHash: String,
    val secretFingerprintHint: String
)

@Serializable
data class RefreshRequest(
    val refreshToken: String,
    val installId: String,
    val deviceFingerprintHash: String
)

@Serializable
data class LogoutRequest(val refreshToken: String)

@Serializable
data class WalletSnapshot(
    val proAvailableCredits: Double = 0.0,
    val premiumAvailableCredits: Double = 0.0,
    val premiumNegativeCredits: Double = 0.0
)

@Serializable
data class StartupSnapshot(
    val userId: String,
    val email: String,
    val emailVerified: Boolean,
    val accessTier: String = "free",
    val phoneVerified: Boolean = false,
    val wallet: WalletSnapshot = WalletSnapshot(),
    val leaseExpiresAtUtc: String? = null,
    val hasResumableLockedSession: Boolean = false,
    val lastLockTokenHash: String = "",
    val lastLockedSessionId: String = "",
    val offlineModeEnabled: Boolean = false,
    val canUseDesktopPowerFeatures: Boolean = false,
    val lastValidatedAtUtc: String? = null,
    val source: String = ""
)

@Serializable
data class PairingCompleteRequest(
    val code: String,
    val companionDeviceId: String,
    val companionDeviceLabel: String,
    val platform: String = "android",
    val appVersion: String
)

@Serializable
data class RelayEnvelope(
    val v: Int = 1,
    val id: String,
    val type: String,
    val ts: String,
    val pairingId: String,
    val role: String = "phone",
    val payload: JsonObject? = null
)
```

For relay frames, either use a polymorphic `payload` object or flatten known fields as optional properties. Unknown fields must not break decode (`ignoreUnknownKeys = true`).

Use `Double` or a decimal serializer for wallet credits. Do not use `Int`.

---

## 18. Retrofit surface

```kotlin
interface PhantomApi {
    @GET("health")
    suspend fun health(): HealthResponse

    @POST("api/desktop/auth/login")
    suspend fun login(@Body body: LoginRequest): AuthSession

    @POST("api/desktop/auth/refresh")
    suspend fun refresh(@Body body: RefreshRequest): AuthSession

    @POST("api/desktop/auth/logout")
    suspend fun logout(@Body body: LogoutRequest): RevokedResponse

    @GET("api/desktop/auth/me")
    suspend fun me(): AuthSession

    @POST("api/desktop/account/startup-check/session")
    suspend fun startupCheck(@Body session: AuthSession): StartupSnapshot

    @POST("api/desktop/auth/forgot-password")
    suspend fun forgotPassword(@Body body: ForgotPasswordRequest): MessageResponse

    @GET("api/companion/pairings")
    suspend fun pairings(): PairingsResponse

    @POST("api/companion/pairings/complete")
    suspend fun completePairing(@Body body: PairingCompleteRequest): Pairing

    @DELETE("api/companion/pairings/{pairingId}")
    suspend fun revokePairing(@Path("pairingId") pairingId: String): RevokedResponse

    @POST("api/companion/relay-ticket")
    suspend fun relayTicket(@Body body: RelayTicketRequest): RelayTicket

    @GET("api/companion/sessions/current")
    suspend fun currentSession(): CompanionSessionSnapshot
}
```

Retrofit `baseUrl` must end with `/`:

`https://phantom-ai-windows-app-backend.onrender.com/`

Auth interceptor: add bearer when a session exists, except for `/health`, `/api/desktop/auth/login`, `/api/desktop/auth/forgot-password`.

Authenticator: on 401, refresh once, retry. If refresh fails, sign out.

---

## 19. Backend implementer note (not the Android agent’s job)

The Android agent does **not** modify the .NET repo.

When companion routes are added to `phantom-windows-app-backend`, they must match sections 7–8 of this file exactly so this Android build starts working.

Desktop still owns:

- `POST /api/companion/pairings/start`
- WebSocket `role=desktop`
- headless capture
- existing `POST /api/desktop/ai/chat`

Until those exist, the Android app is production-ready **as a client**, and Capture & Ask will correctly stay disabled.

---

## 20. Handover template the agent must fill

```markdown
# Phantom Companion Android handover

- Package: com.phantom.companion
- Version:
- API base:
- assembleDebug:
- assembleRelease / minify:

## Endpoints verified
| Method | Path | Result |
| GET | /health | |
| POST | /api/desktop/auth/login | |
| POST | /api/desktop/auth/refresh | |
| POST | /api/desktop/account/startup-check/session | |
| GET | /api/companion/pairings | expected 404 until backend ships |

## How to test with a real account
1. Install debug APK
2. Sign in with a verified Phantom account
3. Confirm Account shows email + tier
4. Confirm Session shows companion-not-enabled if pairings 404
5. After companion APIs ship: pair from desktop Settings → Companion

## Gaps
-
```

---

## 21. Explicit non-goals

- iOS
- Registration
- Live desktop video
- Remote crop lasso
- LAN pairing
- Desktop microphone control
- Changing model/keys from the phone
- Starting an interview from the phone
- Process hiding, overlay spoofing, or any anti-detection work
