# Phantom Implementation Handoff

This file captures the decisions already locked so implementation can start without reopening product debates.

## Confirmed Decisions

### Runtime And Access
- Windows only
- active interviews must continue even if backend connectivity drops
- online start requires backend validation success
- offline start allowed only with a valid local entitlement lease
- launch lease window: `24 hours`
- expired lease blocks new interview starts
- single active interview per account across devices at launch
- same-device offline resume is allowed only when a cached local lock token matches the last active session
- app startup is blocked behind an account-check screen until auth, entitlement, wallet, and lock state finish validation

### Storage
- migrate old `%AppData%/SecureOverlay/` state forward
- write new state under `%AppData%/Windows Host Service 271/`
- do not touch or replace real Windows folders/registry locations
- local persistence moves to SQLite
- local secrets use DPAPI-backed protection
- move logs, WebView2 cache, and temporary speech/auth files into the new app-data root as well
- if migration fails, open read-only safe mode instead of silently resetting user state

### Auth
- auth is mandatory
- phone OTP is mandatory during registration
- admin bypass is backend allowlist by email/phone only
- first-open flow must show `Login` or `Register`
- `Register` opens the website and returns to the desktop app through a magic-link callback
- `Login` remains in-app
- launch login methods: `email/password` and `magic link`
- phone OTP is required during registration before account activation

### Balances
- Pro and Premium use separate balances
- Pro = app-usage credits
- Premium = managed-AI credits
- BYO emergency takeover creates Premium debt

### Metering
- `1 credit = 60 minutes`
- metering block = `15 minutes`
- metering method = wall-clock from session start to session end
- any started `15 minute` block is charged
- zero balance blocks new interview starts
- negative balance is continuation-only, not start-time allowance
- startup requires at least `1 full 15 minute block` available

### Payments
- launch with one-time credit-pack purchases
- no recurring subscription requirement for launch
- no lifetime plan
- launch distribution is a manual downloadable installer
- installer should bootstrap Evergreen WebView2 automatically

### This Repo
- implement desktop foundation here
- implement hosted client/contracts here
- do not implement the real backend service here

## First Implementation Scope

### In Scope
- target folders for new architecture
- local SQLite foundation
- migration from JSON files
- DPAPI secret vault
- repository and service interfaces
- hosted DTOs and client abstractions
- local account cache with lease semantics
- auth gate and callback plumbing for desktop login/register flow
- session lock cache with same-device resume semantics

### Not Yet In Scope
- real backend deployment
- real hosted knowledge processing pipeline
- full website/dashboard implementation
- production payment backend

## Recommended First Coding Slice

1. create new folders:
   - `Application`
   - `Domain`
   - `Infrastructure`
   - `Platform`
2. add SQLite package and migration runner
3. create local database in the new app-data root
4. define repositories for settings, conversations, provider keys, and account cache
5. add one-time migration from old JSON files
6. add DPAPI secret vault
7. add hosted contracts for auth, wallet, entitlements, device locks, usage reconciliation, and auth callback completion
8. add startup account-check gate and read-only safe mode shell

## Launch Defaults

- custom auth callback URI: `phantom://auth/callback`
- register route includes desktop app version and device metadata
- session lock heartbeat interval: `60 seconds`
- session lock TTL: `5 minutes`
- stale lock recovery threshold: `2 missed TTL windows` when online
- protected continuation cap: up to `1.0 Premium credit`
- if offline lease is expired, allow resume of the already locked local session on the same device, but block new interview starts
- signed-in but phone-not-verified users see a verification-required page and cannot access the app
- signed-in users with no credits may enter the app shell but cannot start a new interview
- signed-in users with negative Premium balance may enter the app shell but cannot start a new interview until the debt is cleared
- local device identity at launch uses a generated install ID, a DPAPI-protected device secret, and hashed machine signals for risk scoring; the backend-issued device record remains authoritative
- use a DPAPI-protected local master key plus AES-GCM-encrypted SQLite secret records
- auth, billing, lock, sync, and crash telemetry should be sent to hosted observability services
- admin/support tooling must support stale-lock clears, trial overrides, negative-balance waivers, and reconciliation inspection
- executable/runtime identity and app-data folder naming remain intentionally decoupled as an implementation invariant

## Phase 4 Desktop Seam

- desktop runtime now supports `local` and backend-configured hosted modes through the same startup/auth/account/usage seam
- default mode remains `local` so the Windows app is still runnable without backend dependencies
- runtime environment variables:
  - `PHANTOM_HOSTED_MODE=local|auto|remote`
  - `PHANTOM_WEBSITE_BASE_URL=https://...`
  - `PHANTOM_WINDOWS_BACKEND_BASE_URL=https://...`
- current desktop backend endpoint contract targets:
  - `POST /api/desktop/auth/login`
  - `POST /api/desktop/auth/callback/complete`
  - `POST /api/desktop/account/startup-check/session`
  - `POST /api/desktop/account/startup-check/callback`
  - `POST /api/desktop/usage/reconcile`
- magic-link callback completion is expected to return an authenticated desktop session plus callback state
- in hosted mode, startup revalidates account state against the backend before allowing a fresh launch
- if hosted startup refresh fails, the desktop app may fall back only to a valid cached lease or a resumable locked session on the same device

## Implementation Guardrails

- do not expand `MainWindow.xaml.cs` further for new architecture logic
- keep backward migration one-way: JSON -> SQLite
- no plain-text secrets on disk
- no wallet mutations owned by the desktop client
- no hard stop during active interview continuation scenarios
- do not let migration failure fall through to a fresh writable profile; enter read-only safe mode instead
