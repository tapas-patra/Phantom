# Phantom Monorepo Agent Guide

## Purpose
- This repo is a small monorepo for the Phantom product.
- It contains four active codebases:
  - `phantom-windows-app/`: Windows WPF desktop client
  - `phantom-windows-app-backend/`: .NET 8 backend authority for desktop auth, locks, usage, telemetry, and admin flows
  - `phantom-dashboard-backend/`: .NET 8 query backend for website/dashboard read models
  - `phantom-website-dashboard/`: React/Vite website and user dashboard
- Root-level local orchestration lives in `Start-Phantom-Local.ps1`.

## Repo Shape
- `Start-Phantom-Local.ps1`: opens the local stack in separate PowerShell windows and writes `phantom-windows-app/phantom.hosted.json`.
- `local-dev.env.ps1.example`: canonical local env var template.
- `phantom-windows-app/AGENTS.md`: detailed guide for the Windows app. Read this when the task is desktop-client-specific.
- `phantom-windows-app-backend/CONTRACT-HANDOFF.md`: backend ownership and API contract notes.
- `phantom-dashboard-backend/DOMAIN-HANDOFF.md`: dashboard query ownership boundaries.
- `phantom-website-dashboard/IMPLEMENTATION-HANDOFF.md`: website/dashboard ownership boundaries.

## Stack Summary
- Desktop app: .NET 8, WPF, WebView2, Windows-only runtime
- Backend services: ASP.NET Core on .NET 8 with PostgreSQL via `Npgsql`
- Website/dashboard: React 18 + Vite
- Data store: PostgreSQL is the expected backing store for both backend services

## Read This First
- Desktop/runtime issue:
  - `phantom-windows-app/AGENTS.md`
  - `phantom-windows-app/MainWindow.xaml.cs`
  - `phantom-windows-app/App.xaml.cs`
- Desktop/backend API or auth issue:
  - `phantom-windows-app-backend/Program.cs`
  - `phantom-windows-app-backend/Services/`
  - `phantom-windows-app-backend/Contracts/`
- Dashboard query or admin read-model issue:
  - `phantom-dashboard-backend/Program.cs`
  - `phantom-dashboard-backend/Services/DashboardQueryService.cs`
  - `phantom-dashboard-backend/Persistence/PostgresDashboardStore.cs`
- Website/dashboard UI issue:
  - `phantom-website-dashboard/src/App.jsx`
  - `phantom-website-dashboard/src/lib/api.js`
  - `phantom-website-dashboard/src/styles.css`

## Ownership Boundaries

### `phantom-windows-app/`
- Owns the Windows desktop runtime, WPF UI, local persistence, local vault/state, and desktop-side sync behavior.
- Depends on the hosted backend for auth, startup checks, usage reconciliation, and lock authority.
- Deep desktop guidance already exists in `phantom-windows-app/AGENTS.md`; do not duplicate it when working in that subtree.

### `phantom-windows-app-backend/`
- Owns desktop auth, registration, refresh/logout, magic-link completion, startup checks, usage reconciliation, telemetry ingest, device locks, admin support APIs, Gmail OAuth bootstrap, and test-user seeding.
- Main entrypoint: `phantom-windows-app-backend/Program.cs`
- Key layers:
  - `Contracts/`: request/response DTOs
  - `Services/`: business logic
  - `Persistence/`: PostgreSQL repositories and store wiring
  - `Infrastructure/`: options, validation, filters

### `phantom-dashboard-backend/`
- Owns website/dashboard read models only.
- It should answer account summary, wallet history, device inventory, download entitlement, support preview, and dashboard admin queries.
- It should not become the source of truth for desktop session locks, launch entitlements, or live wallet mutation.
- Main entrypoint: `phantom-dashboard-backend/Program.cs`

### `phantom-website-dashboard/`
- Owns the public website, auth entrypoints, dashboard UI, magic-link explanation/callback surfaces, and admin dashboard pages.
- Main frontend files are intentionally small:
  - `src/App.jsx`
  - `src/lib/api.js`
  - `src/styles.css`
- Backend integrations:
  - `phantom-windows-app-backend` for auth/desktop-linked actions
  - `phantom-dashboard-backend` for dashboard queries

## Local Development

### Full stack
- Preferred root-level startup path on Windows:
  - `powershell -ExecutionPolicy Bypass -File .\Start-Phantom-Local.ps1`
- Optional seeding:
  - `powershell -ExecutionPolicy Bypass -File .\Start-Phantom-Local.ps1 -SeedUsers`
- The script:
  - loads `local-dev.env.ps1` if present
  - requires `dotnet` and `npm`
  - starts windows backend on `http://localhost:5057`
  - starts dashboard backend on `http://localhost:5067`
  - starts the website via Vite
  - builds and launches the desktop app

### Per-project commands
- Windows backend:
  - `cd phantom-windows-app-backend`
  - `dotnet restore`
  - `dotnet run`
- Dashboard backend:
  - `cd phantom-dashboard-backend`
  - `dotnet restore`
  - `dotnet run --urls http://localhost:5067`
- Website/dashboard:
  - `cd phantom-website-dashboard`
  - `npm install`
  - `npm run dev`
- Windows app:
  - `cd phantom-windows-app`
  - `dotnet build SecureOverlay.sln -c Debug`

## Important Environment Variables
- Shared/local stack:
  - `PHANTOM_WINDOWS_BACKEND_DATABASE_URL`
  - `PHANTOM_WINDOWS_BACKEND_ADMIN_API_KEY`
  - `PHANTOM_DASHBOARD_BACKEND_DATABASE_URL`
  - `PHANTOM_DASHBOARD_ADMIN_API_KEY`
  - `PHANTOM_WINDOWS_BACKEND_BASE_URL`
  - `PHANTOM_PUBLIC_WEBSITE_BASE_URL`
  - `PHANTOM_WEBSITE_BASE_URL`
  - `VITE_PHANTOM_DASHBOARD_API_BASE_URL`
  - `VITE_PHANTOM_DASHBOARD_ADMIN_API_KEY`
  - `VITE_PHANTOM_WINDOWS_BACKEND_API_BASE_URL`
- Windows backend also has optional but important mail/secret settings in `local-dev.env.ps1.example`.
- Desktop app can read hosted config from environment variables or `phantom-windows-app/phantom.hosted.json`.

## Task Routing
- Desktop UI, provider/model logic, voice input, local persistence:
  - work in `phantom-windows-app/`
  - start with `phantom-windows-app/AGENTS.md`
- Desktop login, token/session, magic-link, lock, telemetry, usage, admin APIs:
  - work in `phantom-windows-app-backend/`
  - start with `Program.cs`, then the relevant service and repository
- Dashboard summary/history/device/admin read endpoints:
  - work in `phantom-dashboard-backend/`
  - start with `Services/DashboardQueryService.cs`
- Marketing pages, dashboard pages, auth entry screens, callback UX:
  - work in `phantom-website-dashboard/`
  - start with `src/App.jsx` and `src/lib/api.js`
- Cross-surface contract changes:
  - check `phantom-windows-app-backend/Contracts/`
  - then check website API usage in `phantom-website-dashboard/src/lib/api.js`
  - then check any desktop callers under `phantom-windows-app/Services/`

## Verification Expectations
- Backend changes:
  - prefer `dotnet build` in the touched backend before closing work
- Frontend changes:
  - prefer `npm run build` in `phantom-website-dashboard`
- Desktop changes:
  - static inspection/build is possible, but true runtime verification requires Windows with appropriate privileges and WebView2
- This workspace may be inspected from a non-Windows environment, so do not overclaim desktop runtime validation if you did not execute it on Windows

## Low-Signal Areas To Skip
- `bin/`
- `obj/`
- generated frontend build output if it appears later
- one-off handoff docs unless the task is about ownership, contracts, or repo boundaries

## Practical Notes For Future Agents
- Do not scan the whole monorepo by default. Route to the relevant subproject first.
- The desktop app is the most complex surface; use its local `AGENTS.md` instead of rediscovering the same architecture.
- Keep the dashboard backend read-only in spirit. If a change starts introducing write authority there, re-check the ownership docs first.
- The fastest way to understand the integrated local flow is:
  - `local-dev.env.ps1.example`
  - `Start-Phantom-Local.ps1`
  - the relevant subproject README
