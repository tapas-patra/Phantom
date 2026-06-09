# Current Root Ownership

Until extraction, everything in the current root belongs to the future `phantom-windows-app` repo unless explicitly marked otherwise.

## Future `phantom-windows-app`

- all WPF `.xaml` and `.cs` files in the root
- `Application/`
- `Domain/`
- `Infrastructure/`
- `Platform/`
- `Helpers/`
- `Services/`
- local runtime storage, lock, sync, telemetry, auth-gate, context-pack, and billing seams

## Future `phantom-windows-app-backend`

Ownership should move there for backend-facing contracts currently under:
- `Infrastructure/Hosted/Contracts/`

The local stub clients remain desktop-owned until real backend extraction exists:
- `Infrastructure/Hosted/LocalHosted*.cs`
- `Infrastructure/Hosted/IHosted*.cs`

## Future `phantom-website-dashboard`

No production web frontend code exists in this root yet.

## Future `phantom-dashboard-backend`

No production dashboard backend code exists in this root yet.
