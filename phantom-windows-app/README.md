# phantom-windows-app

Active Windows desktop app source root.

Current reality:
- this folder is now the active build root for the WPF desktop application
- run desktop build commands from inside `phantom-windows-app/`
- the app now requires the hosted desktop backend; local hosted/demo fallbacks are removed

Owns:
- WPF UI
- local runtime persistence
- local secret vault
- context packs
- local telemetry and sync queues that reconcile to the hosted backend
- offline continuation behavior

Required runtime env vars:
- `PHANTOM_WINDOWS_BACKEND_BASE_URL`
- `PHANTOM_WEBSITE_BASE_URL`
- `PHANTOM_HOSTED_MODE=remote` or unset
