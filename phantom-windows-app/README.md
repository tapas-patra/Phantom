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

Runtime config fallback:
- if environment variables are not visible after elevation, the app also reads `phantom.hosted.json` from the working directory or executable directory
- this repo now ships a production `phantom.hosted.json` and copies it into the build output, so normal builds do not need manual env setup for the current hosted deployment

Local development example:
- `PHANTOM_WINDOWS_BACKEND_BASE_URL=http://localhost:5057`
- `PHANTOM_WEBSITE_BASE_URL=http://localhost:4173`

Hosted deployment example:
- `PHANTOM_WINDOWS_BACKEND_BASE_URL=https://phantom-ai-windows-app-backend.onrender.com`
- `PHANTOM_WEBSITE_BASE_URL=https://phantom-website-dashboard.vercel.app`
