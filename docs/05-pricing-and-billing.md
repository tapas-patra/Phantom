# Phantom Pricing And Billing

## 1. Final Launch Plans

All prices below are intended as GST-inclusive user-facing prices.

### Free Trial
- `₹0`
- account required
- phone OTP required during registration
- 2 trial sessions
- 20 minutes each
- resume-only local attachment
- no context packs
- no hosted knowledge base

### Pro BYO
- user provides provider API keys
- full local product access
- separate Pro app-usage credit balance

Packs:
- `₹699` for `3` Pro credits
- `₹1,499` for `8` Pro credits
- `₹2,499` for `15` Pro credits

### Premium AI
- Phantom pays model cost
- separate Premium managed-AI credit balance
- hosted knowledge base enabled

Packs:
- `₹1,799` for `3` Premium credits
- `₹3,799` for `8` Premium credits
- `₹5,599` for `15` Premium credits

## 2. Credit Semantics

- `1 credit = 60 minutes`
- metering block = `15 minutes = 0.25 credit`
- metering method = wall-clock time from session start to session end
- any started `15 minute` block counts as billable
- new interviews cannot start at zero available balance
- startup requires at least `0.25 credit` available for the first block
- negative balance is allowed only when an already active interview crosses zero

## 3. Balance Types

### Pro Balance
Used for:
- Pro interview access
- BYO app-usage entitlement

Does not normally pay for:
- Phantom-managed inference

### Premium Balance
Used for:
- Premium AI-included interview access
- Phantom-funded emergency continuation for BYO takeover
- Premium protected continuation overage

## 4. Protected Continuation

### Premium
If Premium credits run out during an active interview:
- do not stop the interview
- allow protected continuation
- launch cap: up to `1.0 Premium credit`
- after session end:
  - no new interview may start until negative Premium balance is cleared
  - next Premium purchase may auto-deduct the owed Premium balance

### Pro BYO
If all BYO keys fail or hit quota during an active interview:
- do not stop the interview
- Phantom temporarily covers inference
- launch cap: up to `1.0 Premium credit`
- after session end:
  - user owes Premium balance, not Pro balance
  - debt may be cleared by direct payment or by purchasing a Premium pack

## 5. Offline Rules

- online start is allowed if backend validation succeeds
- offline start is allowed only if the local lease is still valid
- launch lease duration: maximum `24 hours`
- if the lease has expired, block new interview starts
- active interviews continue even if network drops later
- if the same device already holds the active interview lock and the cached local lock token matches the last active session, offline resume of that session is allowed
- if the lease has expired and there is no resumable same-device locked session, show a dedicated offline-expired state and block the app from starting a new interview

## 6. Single Active Interview Rule

At launch:
- only one active interview is allowed per account across all devices
- if another device holds the active interview lock, a new interview start must fail
- recovery/admin flow may later clear stale locks
- session lock heartbeat interval: `60 seconds`
- lock TTL: `5 minutes`
- stale lock recovery threshold: `2 missed TTL windows` when online

## 7. Trial Abuse Rules

- one account required
- phone OTP mandatory during registration
- backend allowlist by email/phone may bypass phone OTP for admin testing only
- device registration required
- backend remains authoritative for trial eligibility

## 8. Checkout Model

Launch billing model:
- one-time credit pack purchases only
- no recurring subscription required for launch
- no lifetime plan

Supported purchase targets:
- Pro packs
- Premium packs
- direct overage settlement for Premium debt

## 9. User-Facing Expectations

The product should clearly communicate:
- Pro and Premium are different balances
- Pro BYO uses user keys
- Premium uses Phantom-managed AI
- if Phantom saves an interview through continuation, the owed Premium balance is settled after the session, not during it
- users with no credits or negative launch eligibility may still enter the app shell, but the product must clearly block starting a new interview until requirements are satisfied
