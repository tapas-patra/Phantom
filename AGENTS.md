# Phantom Monorepo Agent Guide

## Purpose
- This repo is a small monorepo for the Phantom product.
- It contains five active codebases:
  - `phantom-windows-app/`: Windows WPF desktop client
  - `phantom-mac-app/`: native Swift/AppKit and SwiftUI macOS desktop client
  - `phantom-windows-app-backend/`: .NET 8 backend authority for desktop auth, locks, usage, telemetry, and admin flows
  - `phantom-dashboard-backend/`: .NET 8 query backend for website/dashboard read models
  - `phantom-website-dashboard/`: React/Vite website and user dashboard
- Root-level local orchestration lives in `Start-Phantom-Local.ps1`.

## Repo Shape
- `Start-Phantom-Local.ps1`: opens the local stack in separate PowerShell windows and writes `phantom-windows-app/phantom.hosted.json`.
- `local-dev.env.ps1.example`: canonical local env var template.
- `phantom-windows-app/AGENTS.md`: detailed guide for the Windows app. Read this when the task is desktop-client-specific.
- `phantom-mac-app/README.md`: macOS capabilities, build, self-check, and platform limitations.
- `phantom-windows-app-backend/CONTRACT-HANDOFF.md`: backend ownership and API contract notes.
- `phantom-dashboard-backend/DOMAIN-HANDOFF.md`: dashboard query ownership boundaries.
- `phantom-website-dashboard/IMPLEMENTATION-HANDOFF.md`: website/dashboard ownership boundaries.

## Stack Summary
- Windows desktop app: .NET 8, WPF, WebView2
- macOS desktop app: Swift, AppKit, SwiftUI, Apple Speech
- Backend services: ASP.NET Core on .NET 8 with PostgreSQL via `Npgsql`
- Website/dashboard: React 18 + Vite
- Data store: PostgreSQL is the expected backing store for both backend services

## Read This First
- Desktop/runtime issue:
  - `phantom-windows-app/AGENTS.md`
  - `phantom-windows-app/MainWindow.xaml.cs`
  - `phantom-windows-app/App.xaml.cs`
- macOS desktop/runtime issue:
  - `phantom-mac-app/README.md`
  - `phantom-mac-app/Sources/Phantom/PhantomStore.swift`
  - `phantom-mac-app/Sources/Phantom/ContentView.swift`
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

### `phantom-mac-app/`
- Owns the native macOS runtime, AppKit/SwiftUI overlay, Apple Speech input, Keychain-backed local secrets, local persistence, and macOS-side hosted sync behavior.
- Uses the same hosted backend authority and product contracts as the Windows client while keeping platform-specific UI, capture, and speech adapters native.
- Start with `phantom-mac-app/README.md`, then `PhantomStore.swift` for orchestration and the relevant service file.

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
- macOS app:
  - `swift build --disable-sandbox --package-path phantom-mac-app`
  - `./phantom-mac-app/.build/debug/Phantom --self-check`

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
- Both desktop apps can read hosted config from environment variables or their respective `phantom.hosted.json` files.

## Task Routing
- Windows desktop UI, provider/model logic, voice input, local persistence:
  - work in `phantom-windows-app/`
  - start with `phantom-windows-app/AGENTS.md`
- macOS desktop UI, provider/model logic, voice input, local persistence:
  - work in `phantom-mac-app/`
  - start with `phantom-mac-app/README.md` and `Sources/Phantom/PhantomStore.swift`
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
 - then check Windows callers under `phantom-windows-app/Services/`
 - then check macOS callers under `phantom-mac-app/Sources/Phantom/`
- Companion (phone-as-remote-control) relay/pairing/capture/chat work:
 - backend: `phantom-windows-app-backend/Services/CompanionPairingService.cs`, `CompanionRelayTicketService.cs`, `CompanionRelayHost.cs`, and the `/api/companion/*` routes in `Program.cs` (migration `028_companion_pairings`)
 - Windows: `phantom-windows-app/Services/CompanionOrchestrator.cs`, `CompanionRelayClient.cs`, `CompanionCommandHost.cs`, `HeadlessScreenCapture.cs`, and the Companion Mode panel in `SettingsPage.xaml`
 - macOS: `phantom-mac-app/Sources/Phantom/CompanionOrchestrator.swift`, `CompanionRelayClient.swift`, `CompanionCommandHost.swift`, the `captureDisplay(id:)`/`listDisplays()` helpers in `ScreenshotCapture.swift`, and the Companion Mode section in `ContentView.swift`

## Verification Expectations
- Backend changes:
  - prefer `dotnet build` in the touched backend before closing work
- Frontend changes:
  - prefer `npm run build` in `phantom-website-dashboard`
- Windows desktop changes:
  - static inspection/build is possible, but true runtime verification requires Windows with appropriate privileges and WebView2
- macOS desktop changes:
  - run `swift build` and the executable `--self-check`; true overlay, capture, Keychain, and Speech verification requires macOS permissions and native runtime testing
- Do not overclaim either platform's runtime validation from another OS or from build/self-check results alone.

## Low-Signal Areas To Skip
- `bin/`
- `obj/`
- generated frontend build output if it appears later
- one-off handoff docs unless the task is about ownership, contracts, or repo boundaries

## Practical Notes For Future Agents
- Do not scan the whole monorepo by default. Route to the relevant subproject first.
- Desktop clients are the most complex surfaces; use the Windows local `AGENTS.md` or macOS README instead of rediscovering the same architecture.
- Keep the dashboard backend read-only in spirit. If a change starts introducing write authority there, re-check the ownership docs first.
- The fastest way to understand the integrated local flow is:
  - `local-dev.env.ps1.example`
  - `Start-Phantom-Local.ps1`
  - the relevant subproject README

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
