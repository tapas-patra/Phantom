# Phantom Implementation Plan

## 1. Delivery Strategy

This refactor should be incremental. The current app is already functional, so the goal is to replace unstable seams without freezing product work.

Order of work:
1. isolate persistence
2. isolate secrets
3. add local context-pack and knowledge primitives
4. add hosted client contracts and auth/wallet integration points
5. add auth gate and wallet backend integration
6. add credit metering and protected continuation
7. add Premium hosted knowledge features
8. harden installer, telemetry, and support operations

## 2. Phase 0: Repo Preparation

### Goals
- create architecture docs
- define target folders
- avoid further growth of `MainWindow.xaml.cs`

### Tasks
- add `docs/` package
- create `Application`, `Domain`, `Infrastructure`, `Platform`, and `UI` folders
- add interface-first service skeletons
- move new code into target folders only

### Exit Criteria
- new work no longer lands in random root files by default

## 3. Phase 1: Local Persistence Foundation

### Goals
- replace JSON runtime storage with SQLite
- preserve current behavior

### Tasks
- add SQLite package
- add migration runner
- create local schema for settings, sessions, keys, context, and sync queue
- build repositories for:
  - settings
  - conversations
  - provider keys
  - context packs
- migrate from:
  - `%AppData%/SecureOverlay/settings.json`
  - `%AppData%/SecureOverlay/conversation_cache.json`
- write new state to `%AppData%/Windows Host Service 271/`
- move logs, WebView2 cache, and temporary speech/auth files into the new root
- keep one-time import path for old installs
- if migration fails, enter read-only safe mode

### Files Most Affected
- `Services/SettingsManager.cs`
- `Services/ConversationManager.cs`
- `MainWindow.xaml.cs`
- `Services/VoiceInputService.cs`
- `MicrophonePermissionWindow.xaml.cs`
- `FileLogger.cs`

### Exit Criteria
- app no longer depends on JSON as primary state store
- restart recovery still works

## 4. Phase 2: Secret Storage

### Goals
- remove plain-text secret persistence

### Tasks
- create `ISecretVault`
- implement DPAPI-backed Windows vault adapter
- use a DPAPI-protected local master key with AES-GCM-encrypted SQLite secret records
- move provider keys to encrypted storage
- move auth refresh token cache to encrypted storage
- keep DB references to secret records, not raw values

### Exit Criteria
- provider keys are never written to plain JSON or plain SQLite columns

## 5. Phase 3: Context Packs And Local Knowledge

### Goals
- ship the core Pro feature set locally

### Tasks
- add `resume_profiles`, `job_descriptions`, and `context_packs`
- add local document ingestion
- add chunking and FTS-based retrieval
- add pack selection and editing UI
- update prompt builder to use pack state instead of ad hoc settings blobs

### Files Most Affected
- `Services/ConversationManager.cs`
- `SettingsPage.xaml(.cs)`
- `MainWindow.xaml(.cs)`

### Exit Criteria
- Pro users can create and reuse context packs
- local attachments support retrieval beyond a single resume field

## 6. Phase 4: Hosted Client Contracts

### Goals
- define desktop-side hosted contracts before backend integration work begins

### Tasks
- add DTOs for auth, wallet, entitlements, usage sync, device locks, payments, and auth callback completion
- add service interfaces and client abstractions
- add request/response models for:
  - session lock acquire/release
  - wallet snapshot
  - usage reconciliation
  - phone verification state
  - startup account-check result
  - read-only safe mode state

### Exit Criteria
- desktop implementation can code against stable hosted contracts
- backend service implementation remains separate

## 7. Phase 5: Auth Gate And Entitlements

### Goals
- make account and plan state backend-backed

### Tasks
- add first-open auth gate with `Login` and `Register`
- keep `Login` in-app with `email/password` and `magic link`
- open hosted registration flow from `Register`
- register desktop auth callback URI: `phantom://auth/callback`
- pass desktop app version and device metadata to the hosted registration route
- require phone OTP during registration
- support backend allowlist by email/phone for admin bypass
- add account cache locally
- register device identity with backend using a generated install ID, DPAPI-protected device secret, and hashed machine signals for risk scoring
- fetch plan entitlements and wallet snapshot
- enforce trial eligibility from backend
- block app interactivity behind an account-check screen until auth, verification, entitlement, wallet, lease, and lock checks complete
- add explicit startup states for:
  - phone verification required
  - no credits
  - negative Premium balance
  - expired offline lease without resumable session

### Exit Criteria
- user identity and plan checks no longer rely on local-only state

## 8. Phase 6: Credit Metering And Protected Continuation

### Goals
- implement the monetization model safely

### Tasks
- build `ICreditMeteringService`
- write time-block usage rows locally
- meter by wall-clock time from session start to session end
- charge any started `15 minute` block
- require at least one full first block available before a new interview starts
- add Premium negative-credit continuation
- add Pro BYO emergency Phantom takeover
- cap both continuation paths at `1.0 Premium credit` at launch
- add post-session reconciliation
- block next interview on unresolved negative balance
- enforce single active interview per account across devices
- add lock heartbeat, TTL, and stale-lock recovery behavior:
  - heartbeat every `60 seconds`
  - lock TTL `5 minutes`
  - stale lock recovery after `2 missed TTL windows` when online
- allow same-device offline resume only when the cached local lock token matches the last active session

### Exit Criteria
- active interviews are never hard-stopped by credit exhaustion
- the app can reconcile owed usage cleanly after the interview

## 9. Phase 7: Premium Hosted Knowledge

### Goals
- add clear Premium value beyond managed AI

### Tasks
- add hosted KB upload flow
- add document processing pipeline
- store embeddings and metadata in hosted backend
- add managed KB creation workflow
- add Premium-only limits and entitlement checks

### Exit Criteria
- Premium users can create and use hosted knowledge bases across devices

## 10. Phase 8: Payments And Abuse Controls

### Goals
- connect real money flows and stop free-trial farming

### Tasks
- integrate Razorpay one-time purchase checkout for credit packs
- handle payment webhooks
- post ledger entries from trusted backend
- add trial limits by:
  - user
  - device fingerprint
  - phone verification
- add risk flags and cooldown rules

### Exit Criteria
- wallet changes only through trusted backend logic

## 11. Phase 9: Hardening

### Goals
- make the new architecture operationally safe

### Tasks
- add offline lease logic
- add sync retry and dead-letter handling
- add crash-safe session finalization
- add support tooling for ledger/session inspection
- improve structured logging and audit event capture
- send hosted telemetry for auth, billing, lock, sync, and crash events
- support admin operations for stale-lock clears, trial overrides, negative-balance waivers, and reconciliation inspection
- ship a manual downloadable installer that bootstraps Evergreen WebView2 automatically

### Exit Criteria
- failures are diagnosable and do not silently corrupt money or session state

## 12. Immediate Refactor Targets In This Repo

These are the first code seams to create:

### A. Persistence seam
- replace `SettingsManager` static file access with repository-backed service calls

### B. Session seam
- make `ConversationManager` persist through `IConversationRepository`

### C. Billing seam
- keep `MainWindow.xaml.cs` out of wallet logic
- use `IWalletService` and `ICreditMeteringService`
- separate Pro and Premium balances in the contract and local cache

### D. Secret seam
- keep provider key reads behind `ISecretVault`

### E. Context seam
- stop storing all user context directly on a single settings object

## 13. Suggested New Packages

Phase-dependent package additions:
- `Microsoft.Data.Sqlite`
- `Dapper` if preferred over manual mapping
- a Windows DPAPI helper if you do not want to wrap native APIs yourself

Do not add hosted SDKs directly to every layer. Keep hosted clients inside `Infrastructure/Hosted`.

## 14. Risks

### Data Migration Risk
- old users may have incomplete or malformed settings JSON
- mitigation: migration log + fallback defaults + one-time backup copy

### Billing Drift Risk
- client and server may disagree on time usage
- mitigation: server-authoritative final ledger, local usage audit trail, and explicit wall-clock metering semantics

### UI Regression Risk
- `MainWindow.xaml.cs` is very large and tightly coupled
- mitigation: extract new flows behind services before moving UI behavior

### Secret Handling Risk
- keys may leak through logs or crash traces
- mitigation: scrub logging paths and centralize secret access

## 15. Recommended Build Order For The Actual Code

1. add docs and target folders
2. add repository interfaces
3. add SQLite migration runner
4. migrate settings and conversation cache
5. add DPAPI secret vault
6. add context pack entities and local retrieval
7. add hosted DTOs and client interfaces
8. add auth gate, callback, and wallet client
9. add metering, continuation logic, and session locks
10. add hosted KB and payment flows
11. add telemetry, admin tooling hooks, and installer hardening

## 16. Definition Of Done For The Architecture Migration

The refactor is successful when:
- local state is stored in SQLite
- secrets are encrypted and no longer plain-text on disk
- context packs are first-class local entities
- Premium entitlements come from backend
- active interviews survive credit exhaustion
- post-session reconciliation works
- new features land in structured folders and service layers instead of root-level utility sprawl
