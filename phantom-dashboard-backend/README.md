# phantom-dashboard-backend

Query backend for the Phantom website and dashboard.

Owns:
- account summary queries
- wallet history queries
- device inventory views
- desktop download entitlement views
- support/admin dashboard read models

Not owned here:
- desktop session lock authority
- desktop launch entitlement authority
- live wallet mutation used by the Windows runtime

Run:
```bash
dotnet restore
dotnet run --urls http://localhost:5067
```

Environment:
- `PHANTOM_DASHBOARD_BACKEND_DATABASE_URL`
- falls back to `PHANTOM_WINDOWS_BACKEND_DATABASE_URL`
- `PHANTOM_DASHBOARD_ADMIN_API_KEY`
- `PHANTOM_WINDOWS_BACKEND_BASE_URL`
- `PHANTOM_WINDOWS_BACKEND_ADMIN_API_KEY`

Recommended PostgreSQL connection string form:
```bash
PHANTOM_DASHBOARD_BACKEND_DATABASE_URL="Host=db.your-project.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=YOUR_PASSWORD;SSL Mode=Require"
```

Endpoints:
- `GET /health`
- `GET /api/dashboard/account-summary?email=...`
- `GET /api/dashboard/wallet-history?userId=...`
- `GET /api/dashboard/devices?userId=...`
- `GET /api/dashboard/download-entitlement?userId=...`
- `GET /api/dashboard/support/preview?userId=...`
- `GET /api/dashboard/admin/overview` with `X-Phantom-Admin-Key`
- `GET /api/dashboard/admin/managed-ai/credentials` with `X-Phantom-Admin-Key`
- `POST /api/dashboard/admin/managed-ai/credentials` with `X-Phantom-Admin-Key`
- `DELETE /api/dashboard/admin/managed-ai/credentials/{credentialId}` with `X-Phantom-Admin-Key`
