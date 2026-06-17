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
