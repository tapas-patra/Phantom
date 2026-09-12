# Phantom Companion Mobile App

**Status:** planning only. This document is the product and engineering spec. It is not an implementation ticket.

**Android production brief for Google AI Studio:** [`companion-android-google-ai-studio.md`](./companion-android-google-ai-studio.md). Use that file as the paste-in prompt. Production API base: `https://phantom-ai-windows-app-backend.onrender.com`.

**Backend + Windows + macOS implementation spec:** [`companion-backend-and-desktop.md`](./companion-backend-and-desktop.md). That is what this monorepo must ship before Capture & Ask works.

**Decision already locked:** the phone never talks to the desktop over the same Wi-Fi. All live traffic goes through a **hosted relay** on `phantom-windows-app-backend`.

---

## 1. What we are building

Phantom Companion is a phone app that becomes the remote control and answer screen for a signed-in Windows or macOS desktop client.

The desktop keeps doing the real work:

- screen capture
- prompt assembly
- provider / model routing
- hosted or BYO generation
- interview lock, heartbeat, and billing
- knowledge-base retrieval

The phone does the human work:

- trigger capture
- type or speak a follow-up
- read the streamed answer
- cancel, new-topic, and session status

The desktop can stay out of the foreground. The user does not need to click the overlay, open a capture picker, or type on the computer.

This is for sessions where the computer's foreground app must stay untouched: fullscreen calls, presentations, and other hands-off desktop use. Companion Mode is a remote-control product, not a promise that Phantom is invisible to every OS, conferencing tool, or monitoring environment.

---

## 2. Why a hosted relay

Same-LAN pairing is simpler, but it fails the moment the phone is on cellular, a guest network, a different VLAN, or a locked-down corporate Wi-Fi. The product requirement is: **phone and desktop can be on completely different networks**.

The hosted relay is the meeting point:

```text
Phone  --WSS-->  Phantom Windows Backend (relay room)  <--WSS--  Desktop
                         |
                         +-- existing REST: auth, locks, AI chat, speech, KB
```

Rules:

- The relay is only a signed, account-scoped message bus.
- The relay does **not** become a second AI generator.
- The relay does **not** replace the existing interview lock.
- The relay must work from cellular and from hotel / exam-center / office networks that only allow HTTPS outbound.

There is no mDNS, no local TCP port, no QR-over-LAN bootstrap, and no "enter the computer's IP" flow.

---

## 3. How this fits the current product

Phantom already has the pieces a companion app should reuse:

| Existing capability | Owner today | Companion reuse |
| --- | --- | --- |
| Email / password, magic link, refresh | `phantom-windows-app-backend` | Phone login |
| Phone OTP during registration | backend + website | Account already exists; mobile does not invent a new identity |
| Device install IDs and revoke | backend + dashboard | Companion is a new device class |
| One active interview lock per account | `LockService` | Desktop remains the lock holder; phone is a sidecar |
| Hosted AI chat with up to 3 images | `POST /api/desktop/ai/chat` | Desktop still sends this after capture |
| Speech transcription | `POST /api/desktop/speech/transcribe` | Phone can record audio and send it, or transcribe on-device |
| Context packs, resume, JD, KB | desktop + backend | Desktop keeps assembling prompts |
| Overlay hide / compact bar | Windows + macOS clients | Companion Mode hides the overlay and listens for relay commands |

Current gap: the backend is REST-only. There is no WebSocket or SignalR hub. Companion requires a new live transport.

Invariant that must not break: **only one active interview per account**. The phone must never acquire a desktop interview lock of its own.

---

## 4. Roles

### 4.1 Desktop (worker)

The signed-in Windows or macOS app is the only machine that can see the target screen.

In Companion Mode it must:

1. Stay signed in and keep the existing interview lock + 60-second heartbeat.
2. Open one authenticated WebSocket to the hosted relay.
3. Accept commands without creating a foreground window.
4. Capture the selected display with the existing silent full-display path, not the click-and-drag picker.
5. Attach the image to the current conversation and call the existing chat pipeline.
6. Stream tokens back through the relay.
7. Keep conversation state, BYO keys, and local settings on the desktop.

Desktop must not:

- steal focus
- open the screenshot picker
- open settings, confirm dialogs, or permission prompts during an active companion session
- show the overlay unless the user explicitly exits Companion Mode

Permission prompts (macOS Screen Recording, microphone, Speech) are a **setup-time** problem. They have to be granted before the user enters Companion Mode.

### 4.2 Phone (controller + viewer)

The phone is a thin, always-visible client.

It can:

- sign in with the same Phantom account
- pair to one desktop
- see desktop presence: online, companion-ready, capturing, generating, error
- trigger full-display capture
- trigger capture-and-ask
- send a text follow-up
- send a voice follow-up recorded on the phone
- watch the answer stream
- cancel the current generation
- start a new topic / clear context
- pick which display to capture when the desktop reports more than one
- unpair / sign out

It cannot:

- capture the exam/interview screen itself
- hold BYO provider keys
- become a second billable interview
- talk to the desktop if the desktop is offline

### 4.3 Hosted backend (authority + relay)

`phantom-windows-app-backend` stays the authority for auth, locks, entitlements, and managed AI.

New jobs:

- issue and revoke companion pairings
- host the WebSocket relay
- authorize both sockets against the same user and an approved pairing
- bind a live companion room to the desktop's current interview lock
- rate-limit capture and chat commands
- never persist screenshot bytes or answer text as a chat archive

`phantom-dashboard-backend` later exposes read-only companion device inventory. It does not own the live socket.

`phantom-website-dashboard` later shows paired phones and a revoke button next to existing desktop device sign-out.

---

## 5. Connection model

### 5.1 Two channels, not one

The phone uses two backend channels:

1. **REST** for login, refresh, pairing, device list, revoke.
2. **WebSocket** for live commands and streamed answers.

The desktop uses the same split: existing REST for locks / AI / usage, plus one new WebSocket for companion.

The AI image itself should **not** ride the relay as a full-resolution bitmap. Desktop already knows how to send images through `DesktopAiChatRequestDto`. The relay carries:

- small control messages
- streamed text
- an optional tiny JPEG thumbnail so the phone user can confirm "yes, that is the right screen"

### 5.2 Room identity

A live room is:

```text
userId + pairingId + desktopSessionId
```

Admission rules:

- desktop socket must present a desktop access token whose `DeviceInstallId` matches the pairing's desktop
- phone socket must present a companion access token whose companion device id matches the pairing
- desktop must currently hold the account interview lock, or be in the short pre-lock "companion idle" state used only for pairing/presence
- at most one live phone socket and one live desktop socket per pairing
- a second phone is rejected until the first disconnects or is revoked

### 5.3 Transport

- `wss://<PHANTOM_WINDOWS_BACKEND_BASE_URL>/api/companion/relay`
- subprotocol: `phantom.companion.v1`
- JSON text frames
- ping / pong every 20 seconds; server drops the socket after 45 seconds of silence
- reconnect with exponential backoff: 1s, 2s, 4s, 8s, 15s, cap 15s
- resume token: last `eventId` so the phone can request missed chat deltas after a brief cellular blip
- no message larger than 64 KB on the relay, except the optional thumbnail frame which is capped at 80 KB JPEG

TLS terminates at the existing hosted backend. Do not invent a second public hostname unless production later needs a dedicated websocket node.

### 5.4 Why not the phone calling `/api/desktop/ai/chat` directly

That would skip the desktop conversation manager, local context pack, resume/JD, BYO keys, screenshot source, and lock heartbeat. The phone would generate answers about a screen it cannot see, with a prompt the desktop did not build.

The phone sends **intent**. The desktop executes **the existing interview pipeline**.

---

## 6. Pairing and login

Pairing happens before the hands-off session, while the user can still look at both screens.

### 6.1 Phone login

Reuse desktop account identity, not website cookie auth.

Recommended v1:

1. Email + password against `POST /api/desktop/auth/login`, with a companion device metadata payload.
2. Magic link as v1.1 if password entry on a phone is painful.
3. Refresh through `POST /api/desktop/auth/refresh`.
4. Secure storage: iOS Keychain / Android Keystore. Never `AsyncStorage` for tokens.

Companion login must send a new device class, for example:

```json
{
  "deviceClass": "companion_mobile",
  "platform": "ios",
  "appVersion": "1.0.0",
  "deviceLabel": "iPhone 15"
}
```

Do not reuse a desktop `DeviceInstallId` on the phone. The current lock service compares lock device id to the authenticated desktop session. Mixing those ids would either break the lock or let a phone look like a second desktop.

### 6.2 Desktop pairing offer

In desktop Settings, a **Companion** page:

1. User enables Companion.
2. Desktop calls `POST /api/companion/pairings/start`.
3. Backend returns a 6-character pairing code, a QR payload, and an expiry (10 minutes, single use).
4. Desktop shows the code and QR. This is the one time a visible Phantom window is expected.

The QR payload is not a LAN URL. It is:

```text
phantom-companion://pair?code=AB7K2Q&relay=https://api.example.com
```

The phone app handles that custom scheme. If the app is not installed, a short App Store / Play Store landing page on the website can deep-link after install.

### 6.3 Phone completes pairing

1. User scans QR or types the code.
2. Phone calls `POST /api/companion/pairings/complete` with the code plus its companion device id.
3. Backend binds `userId + desktopDeviceId + companionDeviceId`.
4. Backend returns a long-lived pairing id and a companion device token scoped only to relay + pairing APIs.
5. Both sides store the pairing id.

After that, the code is dead. Reconnects use the pairing id, not the short code.

### 6.4 Later sessions

1. Desktop starts, signs in, acquires or resumes the interview lock, then connects to the relay as `role=desktop`.
2. Phone opens, refreshes tokens, then connects as `role=phone`.
3. Relay puts both sockets in the same room when both are present.
4. Phone shows **Desktop ready** or **Waiting for desktop**.

No second QR is required unless the pairing was revoked or expired from inactivity.

### 6.5 Unpair and revoke

Any of these must kill the live room immediately:

- phone Settings → Unpair
- desktop Settings → Remove phone
- website dashboard → revoke companion device
- backend admin lock / account lock
- desktop interview lock held by a different desktop

Reuse the spirit of `POST /api/desktop/sessions/revoke-device`. Add a companion-specific revoke so a stolen phone cannot keep triggering captures.

---

## 7. Desktop Companion Mode

Companion Mode is a desktop runtime state, not a different binary.

### 7.1 Entering

Preconditions:

- signed in
- startup account check passed
- interview lock acquired or same-device resume allowed
- screen-capture permission already granted
- at least one approved pairing
- relay socket connected

The user turns Companion Mode on from Settings or a dedicated toggle. After that, the overlay hides the same way the current hide shortcut already does.

### 7.2 While active

| Concern | Required behavior |
| --- | --- |
| Overlay | Hidden. No compact bar unless the user exits Companion Mode. |
| Focus | No `Activate()`, no topmost flash, no new dialogs. |
| Capture | Silent full-display / chosen-display capture only. |
| Voice | Desktop mic is off. Phone records follow-ups. |
| Lock | Existing 60-second heartbeat continues. |
| Billing | Existing 15-minute block metering continues on the desktop session. |
| Chat | Same `ConversationManager` / macOS store path as a local send. |
| Errors | Surface on the phone. Do not pop a desktop message box. |

### 7.3 Capture path

Windows today opens `ScreenshotCapture` for click-and-drag. That window is wrong for Companion Mode because it is a visible, focus-taking UI.

Companion capture must call a **headless** full-display helper:

- Windows: existing full-screen capture internals, without showing `ScreenshotCapture`.
- macOS: existing full-display capture, without the in-app preview chrome.

v1 capture is one display. If the desktop has multiple displays, the desktop reports them in `desktop.hello`, and the phone picks `displayId`. Default is the display that currently hosts the fullscreen app, or display 0 if that cannot be determined without extra UI.

v1 does **not** include remote area-selection. Drawing a crop rectangle requires either a desktop overlay or a full-resolution image on the phone. Both are later work.

### 7.4 Platform limits that the product must admit

These are product constraints, not items to "hack around" in this spec:

- macOS Screen Recording permission is a one-time visible system prompt. Grant it during setup.
- Windows capture APIs can still fail under some protected / hardware-accelerated fullscreen paths.
- Some environments block unknown processes, block screen capture, or cut network access. Companion cannot run if the desktop process is not allowed to run, capture, or reach the hosted backend.
- Conferencing tools and OS capture stacks are not a single contract. Current macOS notes already say `sharingType = .none` is not a universal hide guarantee. Companion Mode does not change that.
- The desktop process must already be running. The phone cannot wake a cold machine over the hosted relay.

---

## 8. Mobile features

### 8.1 V1 — ship this first

**Account**

- login
- token refresh
- logout
- pairing via QR or code
- unpair

**Session chrome**

- desktop presence: offline / connecting / ready / capturing / thinking / error
- current provider + model name
- remaining session / wallet summary already available from startup-check style payloads
- selected display name

**Actions**

- **Capture** — desktop grabs the chosen display and keeps it as the pending attachment. Phone shows a low-res thumbnail when available.
- **Ask** — desktop sends the pending screenshot with the default vision prompt ("Please analyze this screenshot.") through the normal chat pipeline.
- **Capture & Ask** — one tap: capture, then send immediately.
- **Follow-up** — text box, send without a new capture, using current conversation context.
- **Stop** — cancel in-flight generation.
- **New topic** — desktop clear-context / new-topic equivalent.

**Answer surface**

- live token stream
- final markdown-ish text, readable on a phone
- last 20 turns of the current desktop conversation, mirrored as text only
- copy answer
- connection-loss banner with automatic reconnect

**Safety**

- confirm Unpair
- lock the Ask buttons when desktop is offline
- disable Capture & Ask when the current model is not vision-capable; show why

### 8.2 V1.1 — same app, next slice

- phone microphone follow-up, transcribed on device or via `POST /api/desktop/speech/transcribe`
- haptic + optional silent banner when the first token arrives
- keep-awake while generating
- display picker when more than one monitor is reported
- "pending screenshot" chip with remove, matching desktop attach/remove

### 8.3 V2 — only after V1 is stable

- saved capture presets ("left half", "right half") configured on desktop beforehand
- interview-type / answer-style switch that already exists on desktop
- regenerate last answer
- push notification when desktop comes online, not for answer tokens
- iPad layout
- optional website companion (read-only answer view) using the same relay protocol

### 8.4 Out of scope

- phone-only Phantom that answers without a paired desktop
- LAN / Bluetooth / USB fallback in v1
- remote desktop streaming / live view of the computer
- remote area lasso
- desktop microphone control from the phone
- changing BYO keys from the phone
- starting a new interview from the phone when no desktop lock exists
- any technique whose purpose is to hide Phantom from OS process lists, accessibility trees, or third-party monitoring software

---

## 9. Phone screens

Keep the app small. Five screens are enough for v1.

1. **Sign in** — email, password, magic-link later.
2. **Pair** — camera QR + manual code. If already paired, skip.
3. **Session** — the working screen:
   - top status pill
   - big **Capture & Ask**
   - secondary **Capture only** and **Stop**
   - transcript
   - composer
4. **Displays / model** — sheet, not a full settings app.
5. **Account** — device name, unpair, sign out, version.

The Session screen is the product. Everything else is setup.

Recommended default interaction:

```text
Open app → confirm Desktop ready → tap Capture & Ask → read stream → type follow-up
```

That is the entire happy path.

---

## 10. Relay protocol

Version header: `v = 1`.

Every frame:

```json
{
  "v": 1,
  "id": "01J...ulid",
  "type": "chat.send",
  "ts": "2026-09-11T00:00:00Z",
  "pairingId": "...",
  "role": "phone"
}
```

### 10.1 Phone → desktop

| `type` | Body | Desktop action |
| --- | --- | --- |
| `session.hello` | `appVersion`, `platform` | Reply with `desktop.hello` |
| `capture.full` | `displayId?`, `attachOnly` | Headless capture; attach if `attachOnly=true` |
| `capture.ask` | `displayId?`, `prompt?` | Capture + send through current conversation |
| `chat.send` | `text` | Send follow-up with current attachments / context |
| `chat.cancel` | `requestId?` | Cancel current stream |
| `chat.new_topic` | — | Desktop new-topic / clear-context |
| `display.select` | `displayId` | Remember selected display for later captures |

### 10.2 Desktop → phone

| `type` | Body | Meaning |
| --- | --- | --- |
| `desktop.hello` | `status`, `model`, `provider`, `vision`, `displays[]`, `lockExpiresAtUtc` | Presence snapshot |
| `desktop.status` | same subset | Later updates |
| `capture.started` | `requestId`, `displayId` | Phone shows capturing |
| `capture.completed` | `requestId`, `width`, `height`, `thumbnailJpegBase64?` | Confirm the right screen |
| `capture.failed` | `requestId`, `code`, `message` | Permission, protected surface, timeout |
| `chat.started` | `requestId`, `turnId` | Match desktop turn ids |
| `chat.delta` | `requestId`, `text` | Append tokens |
| `chat.completed` | `requestId` | Unlock composer |
| `chat.failed` | `requestId`, `code`, `message` | Credits, model, network |
| `chat.cancelled` | `requestId` | User or desktop cancelled |

### 10.3 Relay → both

| `type` | Meaning |
| --- | --- |
| `relay.peer_joined` | The other role connected |
| `relay.peer_left` | The other role disconnected |
| `relay.error` | Auth, lock, rate limit, payload too large |
| `relay.resume_ok` | Replay of missed `eventId`s finished |

### 10.4 Command codes the phone must handle

- `desktop_offline`
- `not_paired`
- `lock_missing` — desktop is signed in but has no interview lock
- `vision_unsupported`
- `capture_permission_missing`
- `rate_limited`
- `pairing_revoked`
- `account_locked`

Do not invent desktop UI for these. The phone is the error surface.

---

## 11. Data paths

### 11.1 Capture & Ask

```text
Phone tap
  -> WSS capture.ask
  -> Relay room
  -> Desktop headless capture
  -> Desktop ConversationManager + existing AI client
       managed: POST /api/desktop/ai/chat with ImagesBase64
       BYO: desktop provider client, same as overlay send
  -> Desktop streams tokens
  -> WSS chat.delta / chat.completed
  -> Phone transcript
```

Screenshot bytes stay on the desktop-to-AI path. The relay sees a command and text tokens. Optional thumbnail is a compressed preview only.

### 11.2 Text follow-up

Same as a desktop send with no new image. Desktop includes the last N messages and current context pack / KB snippets exactly as it does today.

### 11.3 Voice follow-up

Preferred v1.1:

1. Phone records a short clip.
2. Phone transcribes locally, or uploads audio to `POST /api/desktop/speech/transcribe` with the companion token if that API is opened to companion auth.
3. Phone sends `chat.send` with the transcript.

Do not route phone audio through the desktop microphone stack. Windows voice still depends on a hidden WebView2 inside `MainWindow`; macOS uses Speech.framework. Both are the wrong place to hang a remote trigger.

### 11.4 What is stored

| Data | Phone | Relay | Desktop | Dashboard |
| --- | --- | --- | --- | --- |
| Access / refresh tokens | Secure storage | No | Existing | No |
| Pairing id | Yes | Yes | Yes | Device list |
| Full screenshots | No | No | Temporary, as today | No |
| Answer text | Current session cache | Ephemeral in memory | Existing conversation cache | No |
| Thumbnail | Current session only | Pass-through, not archived | Optional | No |

If the phone is killed, it can rehydrate the current transcript from a small `GET /api/companion/sessions/current` snapshot. That snapshot is text-only and dies when the desktop interview ends.

---

## 12. Security

Companion can trigger capture of whatever is on the desktop. Treat pairing like a second unlock for the account.

Required:

- desktop and companion tokens are different audiences
- pairing codes are 10-minute, single-use, rate-limited
- relay tickets are short-lived (5 minutes) and bound to pairing + role
- one active pairing per desktop in v1; more phones is a later change
- revoke drops sockets in under 5 seconds
- capture commands rate-limited: 10 per minute per pairing
- chat sends rate-limited: 30 per minute
- thumbnail max 80 KB; reject anything else
- no screenshot archive table
- audit log: paired, unpaired, relay connected, capture requested, revoked. Do not log prompt text or image bytes
- existing admin / support roles can revoke, not read live answers

Strongly recommended before production:

- pairing creates a device-scoped ECDH session key
- command and chat frames are encrypted with that key
- the relay can route without reading answer text
- thumbnails are encrypted the same way

v1 can ship TLS-only if E2E slips, but the threat model should assume a compromised relay can see tokens and thumbnails unless E2E is on. Do not store those frames in logs either way.

Website cookie auth stays on the website. The phone uses bearer tokens, like desktop, stored in the OS keystore.

---

## 13. Lock, billing, and entitlements

No new wallet.

- The desktop interview is the billable session.
- Companion commands are just another input method into that session.
- If the account cannot start or continue an interview today, the phone shows the same block reason the desktop would show.
- If Premium hits protected continuation, the desktop stays on that path and the phone keeps streaming.
- If the desktop lock expires and is not heartbeaten, the relay closes the room with `lock_missing`.
- A second desktop still cannot steal the lock. A phone attached to the old desktop is disconnected.

Companion does not consume a second "seat." It is not a Free-tier extra interview.

---

## 14. Backend work

All new write/live APIs belong on `phantom-windows-app-backend`.

### 14.1 REST

| Method | Path | Purpose |
| --- | --- | --- |
| `POST` | `/api/companion/pairings/start` | Desktop creates a short code |
| `POST` | `/api/companion/pairings/complete` | Phone consumes the code |
| `GET` | `/api/companion/pairings` | List pairings for the account |
| `DELETE` | `/api/companion/pairings/{pairingId}` | Revoke |
| `POST` | `/api/companion/relay-ticket` | Exchange access token for a short WS ticket |
| `GET` | `/api/companion/sessions/current` | Text-only snapshot for phone resume |

Login / refresh stay on `/api/desktop/auth/*` with a companion device class, or get adjacent `/api/companion/auth/*` wrappers that call the same auth service. Prefer wrappers so companion tokens cannot call desktop-only admin of another machine.

### 14.2 WebSocket

`GET /api/companion/relay?ticket=...` upgrades to WebSocket.

Implementation suggestion: ASP.NET Core WebSockets first, in-process. One backend instance is enough for early usage. If production later has multiple backend replicas, add a Redis backplane or a dedicated relay process. Do not start with SignalR unless we already want its scale-out story; the protocol above is small and custom.

Postgres tables:

- `companion_pairings`
- `companion_devices`
- `companion_audit_events`

Do not add `companion_messages` as a durable chat store.

### 14.3 Dashboard

Later, `phantom-dashboard-backend` projects paired companion devices onto the existing device inventory. Website revoke calls the authority backend, same pattern as `POST /api/desktop/sessions/revoke-device`.

---

## 15. Desktop work

### Windows (`phantom-windows-app`)

New service, not more logic piled into `MainWindow.xaml.cs`:

- `Services/CompanionRelayClient.cs`
- `Services/CompanionCommandHost.cs`
- headless capture helper extracted from `ScreenshotCapture`
- Settings page: pairing QR/code, connected phone, Companion Mode toggle, revoke

`CompanionCommandHost` translates relay frames into the same send / cancel / new-topic methods the UI already uses.

### macOS (`phantom-mac-app`)

Same split:

- relay client
- command host on `PhantomStore`
- Settings pairing surface
- silent full-display capture already exists; hide preview chrome in Companion Mode

Both clients already hide the overlay. Companion Mode is "hide and keep a relay socket," not a new stealth engine.

---

## 16. Mobile app work

New codebase: `phantom-mobile-app/`.

### 16.1 Stack recommendation

**Expo + React Native + TypeScript.**

Reasons:

- iOS and Android from one app
- the website is already React
- secure token storage and camera QR are well-supported
- we do not need native interview overlay code on the phone

Alternatives rejected for v1:

- two native apps: twice the work for a thin remote
- Flutter: fine, but a new language in a .NET / React / Swift monorepo
- mobile web PWA only: possible later as a viewer, weaker camera/QR and background socket behavior

### 16.2 Suggested modules

```text
phantom-mobile-app/
  src/auth/
  src/pairing/
  src/relay/
  src/session/
  src/screens/
  app.json
```

`src/relay` owns the WebSocket, resume, and typed frames. Screens never open raw sockets.

### 16.3 Distribution

- TestFlight + internal Play track first
- same Phantom account as desktop
- website gets a Companion download / pair help page only when the app exists

---

## 17. Failure modes

| Situation | User-visible result |
| --- | --- |
| Desktop app closed | Phone: Waiting for desktop |
| Phone on airplane mode | Local queue of one command max, or just disable send |
| Relay restart | Both reconnect with ticket refresh; phone asks for snapshot |
| Capture permission missing | Phone: setup error, not a retry loop |
| Model has no vision | Capture & Ask disabled; text follow-up still works |
| Credits / trial blocked | Phone shows the desktop block reason |
| Pairing revoked | Socket close; phone returns to Pair |
| Two phones | Second phone is rejected |
| Desktop overlay manually shown | Companion stays connected; capture still headless |
| Backend AI down, BYO still works | Desktop falls back the same way it does today; phone just streams |

Never leave the phone looking successful if the desktop did not actually capture.

---

## 18. Build sequence

Do not start the mobile UI first. The relay and headless capture are the product.

1. **Backend pairing + relay ticket + WebSocket echo** between two test clients.
2. **Windows headless full-display capture** that does not open a window.
3. **Windows command host** wired to existing send / cancel / new-topic.
4. **macOS** same command host.
5. **Expo app** with login, pair, one Session screen, Capture & Ask, stream.
6. **Revoke** on desktop settings and website device list.
7. **Phone voice follow-up.**
8. **E2E frame encryption** if not already in step 1.

Local verification:

- backend: `dotnet` tests around pairing expiry, lock binding, and revoke-kills-socket
- desktop: Windows and macOS must be tested on real machines; this cannot be proven from the other OS
- mobile: two-phone networks (cellular + desktop on home broadband) is the acceptance test that matters

The current monorepo rule still applies: do not claim overlay, capture, or Speech validation from a build machine that is not that OS.

---

## 19. Ownership after this ships

| Surface | Owns |
| --- | --- |
| `phantom-mobile-app/` | Phone UI, pairing UX, relay client |
| `phantom-windows-app/` | Windows companion mode, headless capture, command host |
| `phantom-mac-app/` | macOS companion mode, headless capture, command host |
| `phantom-windows-app-backend/` | Pairing, relay, companion tokens, audit, revoke |
| `phantom-dashboard-backend/` | Read-only companion device inventory |
| `phantom-website-dashboard/` | Pairing help, device revoke UI |

The dashboard backend stays read-only. Live sockets never land there.

---

## 20. Open decisions

These can wait until implementation starts, but they should be chosen on purpose:

1. **Companion auth path:** wrap `/api/desktop/auth/*` vs new `/api/companion/auth/*`.
2. **One phone vs many phones** per desktop. Spec assumes one for v1.
3. **E2E encryption in v1 or v1.1.**
4. **Expo or React Native CLI** once push notifications become real.
5. **Whether Companion Mode may be used without an interview lock** for pairing/presence only. Spec allows idle presence; capture/ask requires a lock.
6. **Thumbnail on or off by default.** Off is more private; on is easier to trust.

---

