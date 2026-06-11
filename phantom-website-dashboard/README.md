# phantom-website-dashboard

React/Vite frontend for:
- public Phantom website
- login/register entrypoints
- desktop magic-link and callback explanation surfaces
- user dashboard for wallet, devices, history, support, and download access

Run:
```bash
npm install
npm run dev
```

Environment:
- `VITE_PHANTOM_DASHBOARD_API_BASE_URL=http://localhost:5067`
- `VITE_PHANTOM_WINDOWS_BACKEND_API_BASE_URL=http://localhost:5057`

Routes:
- `/`
- `/pricing`
- `/download`
- `/login`
- `/register`
- `/magic-link`
- `/desktop-return`
- `/dashboard`

Not owned here:
- wallet mutation authority
- desktop runtime
- session-lock authority
- usage reconciliation writes
