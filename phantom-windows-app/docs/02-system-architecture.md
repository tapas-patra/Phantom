# Phantom System Architecture

## 1. Target Architecture Summary

Phantom remains a single Windows desktop app, but its internals are split into clear layers:
- `UI`: WPF windows, pages, controls, and streaming presentation
- `Application`: interview orchestration, prompts, credits, context packs, sync decisions
- `Infrastructure`: SQLite, secret vault, hosted API clients, payment/auth integration
- `Platform`: Windows-only capabilities such as DPAPI, WebView2, overlay protection, cursor handling

At launch, this can stay inside one `.csproj`. The separation is logical first and physical later.

Implementation invariant:
- executable/runtime identity and app-data folder naming remain intentionally decoupled

## 2. High-Level Component Map

```text
WPF UI
  -> Application Services
      -> Local Data Store (SQLite)
      -> Secret Vault (DPAPI)
      -> Provider Routing + AI Clients
      -> Wallet / Entitlement Client
      -> Sync Service
      -> Hosted Knowledge Client
      -> Windows Platform Adapters
```

## 3. Current Code To Preserve

These files remain important but should stop owning persistence and billing directly:
- `MainWindow.xaml.cs`
- `SettingsPage.xaml.cs`
- `Services/ConversationManager.cs`
- `Services/APIRotationManager.cs`
- `Services/VoiceInputService.cs`
- `Helpers/AIModelRegistry.cs`

Current issues to remove over time:
- oversized UI controller logic in `MainWindow.xaml.cs`
- plain JSON persistence in `Services/SettingsManager.cs`
- app settings object carrying too many responsibilities
- mixed provider, UI, and persistence logic in the same flows

## 4. Proposed Folder Structure

Phase 1 keeps the single project and reorganizes by folder:

```text
/Application
  /Auth
  /Billing
  /Chat
  /Context
  /Sync

/Domain
  /Entities
  /Enums
  /ValueObjects

/Infrastructure
  /Persistence
    /Migrations
    /Repositories
  /Secrets
  /Hosted
  /Payments

/Platform
  /Windows
    /Protection
    /Voice
    /Secrets

/UI
  /Windows
  /Pages
  /ViewModels

/docs
```

Existing files can move into these folders later. First introduce the interfaces, then relocate files.

## 5. Local Architecture

### Local Database
Use `SQLite` as the primary runtime store.

Recommended packages:
- `Microsoft.Data.Sqlite`
- `Dapper` or hand-written repository SQL

### Local Secret Protection
Use:
- `Windows DPAPI` to protect a local master key
- `AES-GCM` for encrypted secret values stored in SQLite

Do not store:
- provider keys
- auth refresh tokens
- local encryption master key
in plain text on disk.

### Local Data Ownership
Local DB owns:
- UI settings
- local interview transcript and cached summaries
- provider configuration and encrypted BYO keys
- context packs
- local documents and chunks
- sync queue
- cached entitlement snapshot

Local storage root at launch:
- migrate existing `%AppData%/SecureOverlay/` users forward
- write new local runtime data under `%AppData%/Windows Host Service 271/`
- do not overwrite real Windows folders or registry locations
- move logs, WebView2 cache, and temporary speech/auth HTML files into the new root too
- if migration fails, enter read-only safe mode instead of silently creating a fresh writable profile

## 6. Hosted Architecture

Use managed backend services for:
- auth
- credit ledger
- payments
- trial enforcement
- Premium hosted knowledge base
- cross-device sync for Premium features

Recommended services:
- `Supabase Auth`
- `Supabase Postgres`
- `pgvector`
- `Supabase Storage`
- `Razorpay`

Hosted operational services should also cover:
- auth callback completion
- structured telemetry for auth, billing, lock, sync, and crash events
- admin/support actions for stale-lock clears, trial overrides, negative-balance waivers, and reconciliation inspection

## 7. Local Schema

### `app_settings`
- `id`
- `selected_ai`
- `voice_input_enabled`
- `window_opacity`
- `use_fake_cursor`
- `fake_cursor_size`
- `system_prompt`
- `debug_mode_enabled`
- `debug_error_simulation`
- `updated_at`

### `provider_keys`
- `id`
- `provider`
- `label`
- `encrypted_key`
- `is_enabled`
- `is_rate_limited`
- `is_invalid`
- `last_used_at`
- `created_at`

### `provider_models`
- `id`
- `provider`
- `model_id`
- `sort_order`
- `is_enabled`
- `is_vision_enabled`

### `rotation_state`
- `provider`
- `last_key_index`
- `last_model_index`
- `last_429_at`

### `resume_profiles`
- `id`
- `name`
- `raw_text`
- `summary`
- `updated_at`

### `job_descriptions`
- `id`
- `company_name`
- `role_name`
- `raw_text`
- `summary`
- `updated_at`

### `context_packs`
- `id`
- `name`
- `resume_profile_id`
- `job_description_id`
- `target_role`
- `company_notes`
- `local_only`
- `updated_at`

### `local_documents`
- `id`
- `file_name`
- `file_path`
- `mime_type`
- `source_kind`
- `sha256`
- `size_bytes`
- `created_at`

### `local_document_chunks`
- `id`
- `document_id`
- `chunk_index`
- `chunk_text`
- `token_count`

### `interview_sessions`
- `id`
- `plan_type`
- `session_mode`
- `state`
- `context_pack_id`
- `started_at`
- `ended_at`
- `protected_continuation_used`
- `continuation_credit_delta`
- `device_lock_id`

### `session_messages`
- `id`
- `session_id`
- `role`
- `content`
- `provider`
- `model`
- `created_at`

### `session_usage_blocks`
- `id`
- `session_id`
- `block_started_at`
- `block_ended_at`
- `minutes_used`
- `credits_used`
- `usage_source`

### `account_cache`
- `id`
- `user_id`
- `plan_code`
- `pro_available_credits_snapshot`
- `premium_available_credits_snapshot`
- `premium_negative_credits_snapshot`
- `last_validated_at`
- `lease_expires_at`
- `phone_verified`
- `verification_state`
- `last_lock_token_hash`
- `last_locked_session_id`

### `sync_queue`
- `id`
- `entity_type`
- `entity_id`
- `action`
- `payload_json`
- `attempt_count`
- `next_attempt_at`
- `created_at`

### `audit_events`
- `id`
- `event_type`
- `severity`
- `payload_json`
- `created_at`

### `device_identity`
- `id`
- `install_id`
- `device_record_id`
- `protected_device_secret`
- `last_machine_fingerprint_hash`
- `created_at`
- `updated_at`

## 8. Hosted Schema

### `user_profiles`
- `id`
- `email`
- `phone`
- `country_code`
- `status`
- `created_at`

### `devices`
- `id`
- `user_id`
- `device_fingerprint_hash`
- `device_name`
- `first_seen_at`
- `last_seen_at`
- `risk_score`
- `active_interview_session_id`

### `wallets`
- `user_id`
- `pro_available_credits`
- `premium_available_credits`
- `premium_negative_credits`
- `trial_status`
- `updated_at`

### `ledger_entries`
- `id`
- `user_id`
- `entry_type`
- `balance_type`
- `credits_delta`
- `amount_inr`
- `session_id`
- `payment_id`
- `metadata_json`
- `created_at`

### `payments`
- `id`
- `user_id`
- `provider`
- `provider_order_id`
- `provider_payment_id`
- `status`
- `gross_amount_inr`
- `net_amount_inr`
- `created_at`

### `entitlements`
- `user_id`
- `plan_code`
- `can_use_context_packs`
- `can_use_byo`
- `can_use_premium_ai`
- `can_use_hosted_kb`
- `hosted_kb_limit_mb`
- `updated_at`

### `interview_usage`
- `id`
- `user_id`
- `session_id`
- `device_id`
- `started_at`
- `ended_at`
- `minutes_used`
- `credits_burned`
- `continuation_credits`
- `balance_type`

### `knowledge_bases`
- `id`
- `user_id`
- `name`
- `status`
- `size_bytes`
- `created_at`

### `kb_documents`
- `id`
- `knowledge_base_id`
- `storage_path`
- `file_name`
- `checksum`
- `status`

### `kb_chunks`
- `id`
- `kb_document_id`
- `chunk_text`
- `embedding`

### `risk_flags`
- `id`
- `user_id`
- `device_id`
- `reason`
- `active`
- `created_at`

## 9. Service Interfaces

Introduce these interfaces before moving major code:

- `ILocalDataStore`
- `ISettingsRepository`
- `IConversationRepository`
- `IProviderKeyRepository`
- `IContextPackRepository`
- `ISecretVault`
- `IAuthService`
- `IWalletService`
- `ICreditMeteringService`
- `ISyncService`
- `ILocalKnowledgeService`
- `IHostedKnowledgeService`
- `IDeviceIdentityService`
- `IAuthCallbackListener`
- `IStartupGateService`
- `ITelemetryService`

These interfaces let current UI code call structured services without rewriting all logic at once.

## 10. Ownership Rules

### Client Owns First
- active interview transcript
- in-progress streaming response
- temporary local retrieval cache

### Server Owns First
- payment status
- wallet balance
- negative credit enforcement
- trial eligibility
- device abuse state
- Premium hosted KB

### Reconciliation Rule
- active session writes are local-first
- final session usage is synced after interview end
- server applies final billing truth

## 11. Key Runtime Flows

### Start Session
1. App startup lands in an auth gate, not the main interview UI.
2. `Register` opens the hosted registration page with desktop app version and device metadata and returns via `phantom://auth/callback`.
3. `Login` remains in-app and supports `email/password` and `magic link`.
4. `IStartupGateService` blocks the app on an account-check screen while auth, phone verification, entitlement, wallet, lease, and lock state are validated.
5. UI asks `IWalletService` for current entitlement snapshot.
6. `IAuthService` refreshes lease if online.
7. `IConversationRepository` creates local interview session row.
8. `ICreditMeteringService` starts usage timer.
9. backend enforces single active interview per account; local session start must fail if another device already holds the lock unless recovery flow clears it
10. startup requires at least one full `15 minute` block of available credit before a new interview may start

### Startup State Gating
- signed in but phone-not-verified: show a verification-required page and block app usage
- signed in with no credits: allow app shell access but block new interview start
- signed in with negative Premium balance: allow app shell access but block new interview start until debt is cleared
- offline with expired lease and no resumable same-device locked session: show a dedicated offline-expired state
- migration failure: show read-only safe mode

### Message Send
1. `ConversationManager` asks context and routing services for prompt state.
2. AI provider call is made.
3. stream is rendered to UI
4. final message is persisted locally

### Premium Credit Exhausted
1. `ICreditMeteringService` detects zero balance during active session.
2. session enters protected continuation mode
3. cheaper fallback route is used if needed
4. overage row is recorded locally
5. final ledger sync happens post-session
6. a new session may not start at zero Premium balance; protected continuation applies only after an already active Premium session crosses zero
7. protected continuation is capped at `1.0 Premium credit` at launch

### Pro BYO Quota Exhausted
1. provider rotation fails across all user keys
2. app switches to Phantom emergency continuation
3. overage is recorded against Phantom-covered credits
4. backend blocks next interview until resolved
5. user may clear the owed Premium-covered balance through direct payment or a Premium pack purchase
6. Phantom emergency continuation is capped at `1.0 Premium credit` at launch

### Same-Device Offline Resume
1. local session cache stores the last active session ID and lock token hash
2. if connectivity is lost after lock acquisition, the active interview may continue locally
3. on restart while offline, the app may resume only if the cached lock token matches the last active session on the same device
4. no new offline session may start when the lease is expired
5. once connectivity returns, lock state is reconciled with the backend

### Lock Timing Defaults
- heartbeat interval: `60 seconds`
- lock TTL: `5 minutes`
- stale lock recovery threshold: `2 missed TTL windows` when online

## 12. Security Decisions

- no plain-text provider keys in SQLite
- no plain-text auth refresh tokens in SQLite
- no hidden billing logic only on the client
- backend verifies session usage writes before final ledger posting
- logs must not contain key prefixes or sensitive raw secrets
- device identity should use a generated install ID, a DPAPI-protected device secret, and hashed machine signals for risk scoring; the backend-issued device record remains authoritative
