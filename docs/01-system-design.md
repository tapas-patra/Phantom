# Phantom System Design

## 1. Goal

Phantom is a Windows desktop interview assistant that must remain reliable during live interviews while supporting:
- stealth overlay usage
- multi-provider LLM routing
- BYO provider keys and Phantom-managed AI usage
- context packs for role-specific preparation
- local and hosted knowledge retrieval
- credit-based billing with protected continuation mid-interview

The design priority is continuity first, monetization second, and refactor safety third:
- never abandon an active interview because billing or auth changes state
- keep private working data local by default
- move money, trial enforcement, and hosted entitlements to the backend
- reorganize the current WPF codebase incrementally without breaking current behavior

## 2. Product Modes

### Free Trial
- account required
- phone OTP required during registration
- 2 trial sessions
- 20 minutes each
- resume-only local attachment
- no context packs
- no hosted knowledge base

### Pro BYO
- credit packs unlock app usage
- user brings provider API keys
- supports up to 3 providers
- supports up to 2 rotating keys per provider
- context packs enabled
- local personal knowledge enabled
- no hosted knowledge base
- uses a dedicated Pro app-usage credit balance

### Premium AI
- credit packs include AI usage
- no API key setup required
- all Pro features included
- hosted knowledge base enabled
- managed knowledge-base creation flow enabled
- protected continuation allowed when credits end mid-interview
- uses a separate Premium managed-AI credit balance

## 3. Core User Flows

### A. Start Interview
1. On first open, the app shows an auth gate with `Login` and `Register`.
2. `Register` opens the hosted registration flow and returns through `phantom://auth/callback`.
3. `Login` stays in-app and supports `email/password` and `magic link`.
4. Phone OTP is required during registration before account activation.
5. After sign-in, the app loads local settings, cached packs, and device state.
6. The app blocks interactivity on an account-check screen while entitlement, wallet, verification, lease, and session-lock state are validated.
7. User chooses interview mode and context pack.
8. App starts a local interview session immediately once allowed.
9. Credit metering begins in 15-minute blocks, with any started block billed.

Offline start rule:
- a new interview may start offline only if the local entitlement lease is still valid
- maximum lease duration at launch: 24 hours from the last successful backend validation
- if the lease has expired, new interview starts are blocked until backend validation succeeds
- once an interview is active, it must continue even if connectivity drops later
- if the same device already holds the active interview lock and the cached local lock token matches the last active session, offline resume of that session is allowed

### B. Continue Mid-Interview
1. User asks a question by text, screenshot, or voice.
2. App builds prompt using active context pack and current session state.
3. Model router picks provider/model.
4. Response is streamed to UI.
5. Session messages and usage blocks are persisted locally.

### C. Limits Hit During Interview
1. If Premium credits run out, enter protected continuation mode.
2. If Pro BYO keys fail, switch to Phantom-funded emergency continuation.
3. App downgrades to the cheapest reliable path if needed.
4. Interview continues to completion.
5. Overage is reconciled after session end.

### D. Post-Interview Reconciliation
1. Session closes.
2. App finalizes local usage blocks and syncs summary to backend.
3. Backend writes wallet and ledger entries.
4. If account is negative, block new interviews until repayment or pack purchase.
5. Only one active interview is allowed per account across all devices.

### E. Startup Account States
1. Signed in but phone not verified: show verification-required page and block the app.
2. Signed in with no credits: allow app shell access but block starting a new interview.
3. Signed in with negative Premium balance: allow app shell access but block starting a new interview until debt is cleared.
4. Offline with expired lease and no resumable same-device locked session: show dedicated offline-expired page.
5. Migration failure: open read-only safe mode.

## 4. Domain Boundaries

### Local-Only Domain
Lives on the Windows machine and must keep working during short network outages:
- UI state
- local settings
- provider key storage
- current interview transcript
- current context pack state
- local documents and retrieval cache
- temporary audio/screenshot workflow state

### Hosted Domain
Must be authoritative for money, abuse control, and Premium cloud features:
- auth identity
- device registration and risk
- trial eligibility
- wallet and ledger
- payment records
- entitlements
- Premium hosted knowledge base
- sync snapshots

## 5. Design Principles

### Continuity Over Strict Billing
- active interviews are never hard-stopped by exhausted credits or BYO quota
- overage is resolved after the interview

### Local-First Runtime
- the app must remain usable during a temporary backend outage
- active sessions are stored locally first, then synced

### Server-Authoritative Monetization
- wallet, trial status, device abuse checks, and payment status come from backend
- client caches these values but does not own truth
- new interviews are blocked at zero available balance; negative balance is allowed only for continuation of an already active session

### Explicit Upgrade Value
- Free Trial is intentionally narrow
- Pro unlocks the full local product
- Premium adds hosted convenience and managed AI

### Incremental Refactor
- do not rewrite the WPF app in one pass
- move persistence, auth, credits, and sync behind interfaces first

## 6. Non-Functional Requirements

### Reliability
- survive app crash and resume local session state
- cleanup should be idempotent
- a failed provider call must not corrupt session history

### Security
- API keys and auth secrets must not live in plain JSON
- use Windows secret protection
- minimize sensitive backend storage for BYO users

### Performance
- local settings/session reads should be sub-100 ms in normal conditions
- message persistence should not block UI streaming
- retrieval should prefer local indexes over repeated summarization

### Observability
- keep structured local logs
- add audit events for auth, wallet, sync, and continuation decisions
- record enough state to debug billing and recovery issues

## 7. Credit Model Rules

- 1 credit = 60 minutes of active interview usage
- metering granularity = 15 minutes = 0.25 credit
- active interview usage is billed by wall-clock time from session start to session end
- any started 15-minute block is billable
- Free Trial uses session caps, not reusable wallet credits
- Pro burns Pro app-usage credits; model cost is mostly externalized to user keys
- Premium burns Premium managed-AI credits
- Premium overage cap at launch: 1.0 credit
- Pro emergency Phantom takeover cap at launch: 1.0 credit
- new interview startup requires at least one full first 15-minute block available
- no new interview may start while negative balance remains unresolved
- Pro BYO emergency takeover debt may be cleared either by direct payment or by purchasing a Premium pack and deducting the owed Premium balance
- single active interview per account is enforced across devices at launch

## 8. Target Outcomes For The Refactor

By the end of the architecture migration, Phantom should have:
- no critical runtime data in loose JSON files
- no plain-text provider keys on disk
- a service layer between UI and persistence
- local context-pack and knowledge workflows
- backend-backed wallet and entitlement logic
- protected continuation as a first-class billing feature
