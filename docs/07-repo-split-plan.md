# Phantom Repo Split Plan

This workspace is being split into four future repositories.

## Target Repositories

### 1. `phantom-website-dashboard`
- purpose: public website, onboarding surfaces, hosted registration flow, user dashboard UI
- stack target: web frontend only
- owns:
  - marketing site
  - login/register pages
  - dashboard pages for credits, devices, downloads, billing history, support
  - desktop deep-link launch and callback initiation UX
- does not own:
  - desktop app runtime
  - wallet authority
  - session lock authority
  - ledger mutations

### 2. `phantom-windows-app`
- purpose: Windows WPF desktop application
- stack target: `.NET 8` WPF + WebView2
- owns:
  - overlay app UX
  - local SQLite runtime
  - local DPAPI secret vault
  - local context packs
  - offline continuation behavior
  - local sync queues, telemetry queue, crash handling
- current source of truth:
  - this repo today

### 3. `phantom-windows-app-backend`
- purpose: backend authority for the desktop app
- stack target: hosted API/backend service
- owns:
  - desktop auth/session completion
  - entitlement checks
  - wallet snapshots for app launch
  - device registration and risk signals
  - single-active-session lock authority
  - usage reconciliation
  - hosted observability ingestion for desktop auth/billing/lock/sync/crash events
- does not own:
  - dashboard page rendering

### 4. `phantom-dashboard-backend`
- purpose: backend for website/dashboard experiences
- stack target: hosted API/backend service
- owns:
  - dashboard data aggregation
  - credit-pack purchase history views
  - device inventory views
  - support/admin dashboard APIs
  - website-facing account/profile/dashboard query APIs
- does not own:
  - desktop lock authority
  - authoritative wallet mutation logic for the app runtime

## Why This Split

- the current codebase is only the Windows app
- desktop runtime concerns and public web/dashboard concerns should not share release cadence
- desktop auth/billing/session rules need a backend with stricter invariants than a general dashboard API
- dashboard/backend evolution should not force desktop runtime deployment coupling

## Extraction Order

1. keep this workspace as the Windows app implementation source
2. freeze repo boundaries and contracts
3. stand up empty future repo roots and handoff docs
4. extract website/dashboard frontend into `phantom-website-dashboard`
5. extract desktop backend contracts into `phantom-windows-app-backend`
6. extract dashboard APIs into `phantom-dashboard-backend`
7. physically relocate the Windows app source into the dedicated `phantom-windows-app` repo only when build verification is available

## Current Implementation Rule

Until the physical extraction happens, all existing code under the current root is treated as belonging to `phantom-windows-app`.

## Contract Boundary

The current `Infrastructure/Hosted/Contracts` folder is the temporary contract seam for:
- desktop auth
- startup account checks
- device locks
- usage reconciliation
- phone verification state
- safe mode state

Those contracts should move to `phantom-windows-app-backend` ownership once that repo exists.

## Immediate Workspace Layout

This repo now carries bootstrap folders for the four future repos under `repos/`.
They are scaffolds and ownership markers, not active build roots yet.
