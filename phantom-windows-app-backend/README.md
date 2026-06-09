# phantom-windows-app-backend

Backend authority for the Windows desktop runtime.

Implemented:
- PostgreSQL-backed account, session, lock, usage, telemetry, and magic-link persistence
- PBKDF2 password hashing
- desktop password login, refresh, logout, and magic-link auth flows
- startup account-check APIs
- usage reconciliation API
- active-session lock acquire/heartbeat/release APIs
- telemetry ingestion API
- admin inspection and support APIs protected by API key
- background cleanup for expired sessions, locks, magic links, and stale login-attempt rows
- seeded pricing-tier users for desktop testing

Current project file:
- `Phantom.WindowsApp.Backend.csproj`

Run from this folder on a machine with .NET 8:
```bash
dotnet restore
dotnet run
```

Default endpoints:
- `GET /health`
- `GET /health/ready`
- `POST /api/desktop/auth/login`
- `POST /api/desktop/auth/refresh`
- `POST /api/desktop/auth/logout`
- `POST /api/desktop/auth/magic-link/request`
- `POST /api/desktop/auth/callback/complete`
- `POST /api/desktop/account/startup-check/session`
- `POST /api/desktop/account/startup-check/callback`
- `POST /api/desktop/usage/reconcile`
- `POST /api/desktop/telemetry/ingest`
- `POST /api/desktop/locks/acquire`
- `POST /api/desktop/locks/heartbeat`
- `POST /api/desktop/locks/release`
- `GET /magic-link/consume?token=...`
- `GET /api/admin/accounts/{userId}`
- `POST /api/admin/locks/clear`
- `POST /api/admin/balance/waive-negative-premium`
- `POST /api/admin/credits/grant`

Required env vars:
- `PHANTOM_WINDOWS_BACKEND_DATABASE_URL`

Recommended env vars:
- `PHANTOM_WINDOWS_BACKEND_ADMIN_API_KEY`
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

Admin endpoints require the `X-Phantom-Admin-Key` header.

Seeded desktop test users:
- `free.user@phantom.app` / `PhantomFree123!`
- `pro.user@phantom.app` / `PhantomPro123!`
- `premium.user@phantom.app` / `PhantomPremium123!`
