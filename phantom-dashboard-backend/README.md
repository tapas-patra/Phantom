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
- `GET /health/details`
- `GET /health/ready`
- `GET /api/dashboard/account-summary` with `Authorization: Bearer <user access token>`
- `GET /api/dashboard/wallet-history` with `Authorization: Bearer <user access token>`
- `GET /api/dashboard/devices` with `Authorization: Bearer <user access token>`
- `GET /api/dashboard/download-entitlement` with `Authorization: Bearer <user access token>`
- `GET /api/dashboard/support/preview` with `Authorization: Bearer <user access token>`
- `GET /api/dashboard/admin/overview` with `Authorization: Bearer <admin access token>`
- `GET /api/dashboard/admin/managed-ai/credentials` with `Authorization: Bearer <admin access token>`
- `POST /api/dashboard/admin/managed-ai/credentials` with `Authorization: Bearer <admin access token>`
- `DELETE /api/dashboard/admin/managed-ai/credentials/{credentialId}` with `Authorization: Bearer <admin access token>`

Production notes:
- The dashboard backend now treats the authority backend as the source of truth for user/admin session validation and admin operational reads.
- `PHANTOM_WINDOWS_BACKEND_BASE_URL` and `PHANTOM_WINDOWS_BACKEND_ADMIN_API_KEY` are required for production use.
- `GET /health/details` exposes authority-backend circuit and connectivity state for operational checks.
- `GET /health/ready` returns `503` when the read database is down or the authority bridge is not ready.
