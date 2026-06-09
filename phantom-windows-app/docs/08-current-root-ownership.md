# Current Root Ownership

The repository root is now divided into four top-level product folders. Ownership is defined by folder.

## `phantom-windows-app`

- all WPF `.xaml` and `.cs` files under `phantom-windows-app/`
- `phantom-windows-app/Application/`
- `phantom-windows-app/Domain/`
- `phantom-windows-app/Infrastructure/`
- `phantom-windows-app/Platform/`
- `phantom-windows-app/Helpers/`
- `phantom-windows-app/Services/`
- local runtime storage, lock, sync, telemetry, auth-gate, context-pack, and billing seams

## `phantom-windows-app-backend`

Ownership should move there for backend-facing contracts currently under:
- `phantom-windows-app/Infrastructure/Hosted/Contracts/`

The local stub clients remain desktop-owned until real backend extraction exists:
- `phantom-windows-app/Infrastructure/Hosted/LocalHosted*.cs`
- `phantom-windows-app/Infrastructure/Hosted/IHosted*.cs`

## `phantom-website-dashboard`

No production web frontend code exists in this root yet.

## `phantom-dashboard-backend`

No production dashboard backend code exists in this root yet.
