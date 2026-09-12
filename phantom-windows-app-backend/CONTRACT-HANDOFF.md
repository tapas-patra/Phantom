# Windows App Backend Contract Handoff

This repo will own the backend authority for desktop runtime behavior.

## Current Contract Source

Temporary contract source in current workspace:
- `phantom-windows-app/Infrastructure/Hosted/Contracts/`

## Contracts To Re-Home Here

- `AuthSessionDto`
- `AuthCallbackResultDto`
- `WalletSnapshotDto`
- `StartupAccountCheckResultDto`
- `PhoneVerificationStateDto`
- `ReadOnlySafeModeStateDto`
- `DeviceRegistrationMetadataDto`
- `DeviceLockAcquireRequestDto`
- `DeviceLockAcquireResultDto`
- `DeviceLockHeartbeatRequestDto`
- `DeviceLockReleaseRequestDto`
- `UsageReconciliationRequestDto`
- `UsageReconciliationResultDto`

## APIs This Repo Must Eventually Expose

### Auth
- desktop login completion
- magic link callback completion
- auth session refresh

### Startup
- startup account check
- entitlement and wallet snapshot
- phone verification gate state
- offline lease state

### Device
- device registration
- device trust/risk evaluation
- same-device resume eligibility

### Locking
- acquire active interview lock
- heartbeat active interview lock
- release active interview lock
- stale-lock clear workflows

### Billing
- usage reconciliation
- negative balance inspection
- continuation debt inspection

### Companion (phone-as-remote-control)
- `POST /api/companion/pairings/start` — issue a short-lived pairing code bound to the desktop device
- `POST /api/companion/pairings/complete` — phone redeems code to finalize a pairing
- `GET /api/companion/pairings` — list active pairings for the desktop session
- `DELETE /api/companion/pairings/{pairingId}` — revoke a pairing (desktop or phone side)
- `POST /api/companion/relay-ticket` — issue a single-use WebSocket relay ticket
- `GET /api/companion/sessions/current` — current companion session snapshot (presence + last turns)
- `GET /api/companion/relay` — WebSocket relay endpoint (subprotocol `phantom.companion.v1`)
- Relay state is in-memory only; one active desktop + one active phone socket per pairing; forwards `capture.*` / `chat.*` / `session.*` / `desktop.*` frames between peers

### Observability
- auth telemetry ingestion
- billing telemetry ingestion
- lock telemetry ingestion
- sync/crash telemetry ingestion

## Desktop Client Dependencies

The Windows app currently assumes:
- startup is blocked until account check completes
- same-device locked resume can be trusted locally only when the backend-issued lock model agrees
- usage reconciliation is an explicit post-session operation
- wallet mutation authority stays on the backend

## Production Security Additions

- Admin sessions require password plus an email OTP challenge. Challenges expire, are single-use, are hashed at rest, and lock after a bounded number of failed attempts.
- Admin mutations are role-gated: `super_admin` has full access; `support_admin` is limited to support updates and interview-lock clearing; other active admin roles are read-only.
- Every allowed admin mutation writes an `admin_action_audit` record with operator, route, target, reason, outcome, IP address, and correlation ID.
- Browser session DTOs are sanitized; bearer and refresh tokens remain in secure HttpOnly cookies.
- Installer links are HMAC-signed, short-lived, account-bound, and re-check email verification and manual-lock state when redeemed.

Required production settings:

- `PHANTOM_WINDOWS_BACKEND_DOWNLOAD_SIGNING_KEY`
- `PHANTOM_WINDOWS_BACKEND_RELEASE_REPOSITORY`
- `PHANTOM_WINDOWS_BACKEND_RELEASE_TAG`
- working Gmail OAuth or SMTP delivery for admin OTP

GitHub Releases remains the current asset origin. A public GitHub release is still discoverable outside Phantom; move the same signed-link service to private object storage when strict asset confidentiality is required.
