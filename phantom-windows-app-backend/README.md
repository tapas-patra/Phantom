# phantom-windows-app-backend

Backend authority for the Windows desktop runtime.

Implemented:
- desktop login completion
- magic-link callback completion
- startup account-check APIs
- usage reconciliation API
- active-session lock acquire/heartbeat/release APIs
- telemetry ingestion API
- admin inspection and support APIs
- SQLite-backed local persistence for accounts, sessions, locks, and usage ledger

Current project file:
- `Phantom.WindowsApp.Backend.csproj`

Run from this folder on a machine with .NET 8:
```bash
dotnet restore
dotnet run
```

Default endpoints:
- `GET /health`
- `POST /api/desktop/auth/login`
- `POST /api/desktop/auth/callback/complete`
- `POST /api/desktop/account/startup-check/session`
- `POST /api/desktop/account/startup-check/callback`
- `POST /api/desktop/usage/reconcile`
- `POST /api/desktop/telemetry/ingest`
- `POST /api/desktop/locks/acquire`
- `POST /api/desktop/locks/heartbeat`
- `POST /api/desktop/locks/release`
- `GET /api/admin/accounts/{userId}`
- `POST /api/admin/locks/clear`
- `POST /api/admin/balance/waive-negative-premium`
- `POST /api/admin/credits/grant`

Optional env vars:
- `PHANTOM_WINDOWS_BACKEND_DB_PATH`
- `PHANTOM_WINDOWS_BACKEND_LEASE_HOURS`
- `PHANTOM_WINDOWS_BACKEND_LOCK_TTL_MINUTES`
- `PHANTOM_WINDOWS_BACKEND_DEFAULT_PRO_CREDITS`
- `PHANTOM_WINDOWS_BACKEND_DEFAULT_PREMIUM_CREDITS`
