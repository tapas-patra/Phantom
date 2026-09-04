# phantom-windows-app-backend

Operational logging and Render search instructions: [`../shared/observability/README.md`](../shared/observability/README.md).

Backend authority for the Windows desktop runtime.

Implemented:
- PostgreSQL-backed account, session, lock, usage, telemetry, and magic-link persistence
- real hosted registration and email-verification persistence
- Gmail API OAuth bootstrap endpoints for production email delivery
- PBKDF2 password hashing
- desktop password login, refresh, logout, and magic-link auth flows
- startup account-check APIs
- usage reconciliation API
- active-session lock acquire/heartbeat/release APIs
- telemetry ingestion API
- admin inspection and support APIs protected by admin session or backend API key
- background cleanup for expired sessions, locks, magic links, and stale login-attempt rows
- explicit database seeding path for test users

Current project file:
- `Phantom.WindowsApp.Backend.csproj`

Run from this folder on a machine with .NET 8:
```bash
dotnet restore
dotnet run
```

Seed the three test users into the configured PostgreSQL database:
```bash
dotnet run -- --seed-test-users
```

You can also apply [db/seed-test-users.sql](/Users/tapaskumarpatra/TKP-Other-personal/Phantom/phantom-windows-app-backend/db/seed-test-users.sql) directly in Supabase SQL Editor. It now includes `desktop_accounts` bootstrap and `ALTER TABLE ... ADD COLUMN IF NOT EXISTS ...` statements so it can upgrade older schemas before inserting users.

Default endpoints:
- `GET /health`
- `GET /health/details`
- `GET /health/ready`
- `POST /api/desktop/auth/register`
- `POST /api/desktop/auth/login`
- `POST /api/desktop/auth/verify-email/request`
- `POST /api/desktop/auth/refresh`
- `POST /api/desktop/auth/logout`
- `POST /api/desktop/auth/magic-link/request`
- `POST /api/desktop/auth/callback/complete`
- `POST /api/desktop/account/startup-check/session`
- `POST /api/desktop/account/startup-check/callback`
- `GET /api/desktop/kb`
- `POST /api/desktop/kb`
- `POST /api/desktop/kb/documents`
- `POST /api/desktop/kb/reindex`
- `GET /api/desktop/kb/search`
- `POST /api/desktop/usage/reconcile`
- `POST /api/desktop/telemetry/ingest`
- `POST /api/desktop/locks/acquire`
- `POST /api/desktop/locks/heartbeat`
- `POST /api/desktop/locks/release`
- `GET /magic-link/consume?token=...`
- `GET /email/verify?token=...`
- `GET /api/admin/accounts/{userId}`
- `POST /api/admin/auth/login`
- `POST /api/admin/auth/refresh`
- `POST /api/admin/auth/logout`
- `GET /api/admin/auth/me`
- `POST /api/admin/auth/forgot-password`
- `POST /api/admin/auth/reset-password`
- `GET /api/admin/kb/embedding-config`
- `POST /api/admin/kb/embedding-config`
- `POST /api/admin/locks/clear`
- `POST /api/admin/balance/waive-negative-premium`
- `POST /api/admin/credits/grant`

Required env vars:
- `PHANTOM_WINDOWS_BACKEND_DATABASE_URL`

Recommended connection string form for Supabase:
```bash
PHANTOM_WINDOWS_BACKEND_DATABASE_URL="Host=db.your-project.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=YOUR_PASSWORD;SSL Mode=Require"
```

If you prefer URI form and the password contains `@`, encode it as `%40`.

Recommended env vars:
- `PHANTOM_PUBLIC_WEBSITE_BASE_URL`
- `PHANTOM_WINDOWS_BACKEND_INTERNAL_API_KEY`
- `PHANTOM_WINDOWS_BACKEND_SECRET_ENCRYPTION_KEY`
- `PHANTOM_WINDOWS_BACKEND_DOWNLOAD_SIGNING_KEY`
- `PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_ENABLED`
- `PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_PROVIDER`
- `PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_BASE_URL`
- `PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_API_KEY`
- `PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_MODEL`
- `PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_DIMENSIONS`
- `PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_VERSION`
- `PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_BATCH_SIZE`
- `PHANTOM_WINDOWS_BACKEND_GOOGLE_OAUTH_CLIENT_SECRETS_PATH`
- `PHANTOM_WINDOWS_BACKEND_GOOGLE_OAUTH_REDIRECT_URI`
- `PHANTOM_WINDOWS_BACKEND_GOOGLE_OAUTH_REFRESH_TOKEN` (cold-deployment email bootstrap; store only as a secret)
- `PHANTOM_WINDOWS_BACKEND_LEASE_HOURS`
- `PHANTOM_WINDOWS_BACKEND_LOCK_TTL_MINUTES`
- `PHANTOM_WINDOWS_BACKEND_DEFAULT_PRO_CREDITS`
- `PHANTOM_WINDOWS_BACKEND_DEFAULT_PREMIUM_CREDITS`
- `PHANTOM_WINDOWS_BACKEND_SESSION_TTL_HOURS`
- `PHANTOM_WINDOWS_BACKEND_MAGIC_LINK_TTL_MINUTES`
- `PHANTOM_WINDOWS_BACKEND_PASSWORD_ITERATIONS`
- `PHANTOM_WINDOWS_BACKEND_LOGIN_ATTEMPT_WINDOW_MINUTES`
- `PHANTOM_WINDOWS_BACKEND_MAX_FAILED_LOGIN_ATTEMPTS`
- `PHANTOM_WINDOWS_BACKEND_SMTP_HOST`
- `PHANTOM_WINDOWS_BACKEND_SMTP_PORT`
- `PHANTOM_WINDOWS_BACKEND_SMTP_USERNAME`
- `PHANTOM_WINDOWS_BACKEND_SMTP_PASSWORD`
- `PHANTOM_WINDOWS_BACKEND_SMTP_FROM_EMAIL`
- `PHANTOM_WINDOWS_BACKEND_SMTP_FROM_NAME`
- `PHANTOM_WINDOWS_BACKEND_SMTP_ENABLE_SSL`
- `PHANTOM_WINDOWS_BACKEND_OTP_PROVIDER`
- `PHANTOM_WINDOWS_BACKEND_OTP_API_KEY`
- `PHANTOM_WINDOWS_BACKEND_OTP_TEMPLATE_NAME`
- `PHANTOM_WINDOWS_BACKEND_MOCK_OTP_CODE`
- `PHANTOM_BOOTSTRAP_ADMIN_EMAIL`
- `PHANTOM_BOOTSTRAP_ADMIN_PASSWORD`
- `PHANTOM_BOOTSTRAP_ADMIN_DISPLAY_NAME`
- `PHANTOM_WINDOWS_BACKEND_ALLOW_IMPLICIT_LOCAL_ADMIN_BOOTSTRAP`
- `PHANTOM_WINDOWS_BACKEND_ALLOW_TEST_USER_SEEDING`

Development-only mock OTP:
```bash
ASPNETCORE_ENVIRONMENT=Development
PHANTOM_WINDOWS_BACKEND_OTP_PROVIDER=mock
PHANTOM_WINDOWS_BACKEND_MOCK_OTP_CODE=111111
```

`mock` OTP is intentionally blocked outside `Development`.

Hosted knowledge-base retrieval is designed to stay independent from the live chat model. Recommended production embedding profile:

```bash
PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_ENABLED=true
PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_PROVIDER=openai
PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_BASE_URL=https://api.openai.com/v1
PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_MODEL=text-embedding-3-small
PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_DIMENSIONS=1536
PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_VERSION=1
```

These env vars are now bootstrap defaults. After the first admin save through the dashboard, the persisted admin profile becomes the active KB embedding configuration and overrides env defaults.

Changing chat providers does not require KB reindexing. Changing the KB embedding model should happen through the admin embedding panel with a version bump and `/api/desktop/kb/reindex`.

Current storage supports variable embedding dimensions. Configure the exact output dimension for the selected model and keep the endpoint OpenAI-compatible at `/embeddings`.

Browser admin auth uses the `/api/admin/auth/*` password-plus-email-OTP session endpoints. Backend-to-backend session validation uses `PHANTOM_WINDOWS_BACKEND_INTERNAL_API_KEY`; static admin keys are not accepted by admin routes.

Production notes:
- `PHANTOM_WINDOWS_BACKEND_ALLOW_IMPLICIT_LOCAL_ADMIN_BOOTSTRAP` defaults to `false` and should stay `false` outside local recovery scenarios.
- `PHANTOM_WINDOWS_BACKEND_ALLOW_TEST_USER_SEEDING` defaults to `false`; test-user seeding now requires explicit opt-in.
- `GET /health/details` exposes worker status for telemetry buffering, projection replication, and maintenance cleanup.
- `GET /health/ready` now returns `503` when the database is down or worker degradation crosses readiness thresholds.

Gmail OAuth bootstrap endpoints:
- `GET /api/admin/integrations/gmail/oauth/status`
- `POST /api/admin/integrations/gmail/oauth/start`
- `GET /api/admin/integrations/gmail/oauth/callback`

Default test users after explicit seeding:
- `free.user@phantom.app` / `PhantomFree123!`
- `pro.user@phantom.app` / `PhantomPro123!`
- `premium.user@phantom.app` / `PhantomPremium123!`
