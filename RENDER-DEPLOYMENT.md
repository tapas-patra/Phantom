# Render Deployment

This repo is prepared for:

- `phantom-windows-app-backend` on Render Free as a Docker web service
- `phantom-dashboard-backend` on Render Free as a Docker web service
- `phantom-website-dashboard` staying on Vercel

Expected Render service URLs:

- `https://phantom-ai-windows-app-backend.onrender.com`
- `https://phantom-dashboard-backend.onrender.com`

## Why the keepalive exists

Render documents that Free web services spin down after 15 minutes without inbound traffic. Render also documents that Free instance types are not available for cron jobs.

Because of that, this repo includes a GitHub Actions workflow at [.github/workflows/render-keepalive.yml](/Users/tapaskumarpatra/TKP-Other-personal/Phantom/.github/workflows/render-keepalive.yml:1) that pings both `/health` endpoints every 10 minutes.

This is a best-effort workaround, not a hard uptime guarantee. GitHub scheduled workflows can drift.

## Deploy steps

1. In Render, create a new Blueprint from this repo, or create two Docker web services manually using the same settings from [render.yaml](/Users/tapaskumarpatra/TKP-Other-personal/Phantom/render.yaml:1).
2. Keep the service names exactly as:
   - `phantom-ai-windows-app-backend`
   - `phantom-dashboard-backend`
3. Set the required secret env vars in Render for both services.
4. Redeploy the Vercel website with the backend URLs from [phantom-website-dashboard/.env](/Users/tapaskumarpatra/TKP-Other-personal/Phantom/phantom-website-dashboard/.env:1).
5. Rebuild the Windows app so the checked-in hosted config points at the Render backend.

## Required env vars in Render

Windows backend:

- `PHANTOM_WINDOWS_BACKEND_DATABASE_URL`
- `PHANTOM_WINDOWS_BACKEND_INTERNAL_API_KEY`
- `PHANTOM_WINDOWS_BACKEND_SECRET_ENCRYPTION_KEY`
- `PHANTOM_WINDOWS_BACKEND_DOWNLOAD_SIGNING_KEY`
- `PHANTOM_WINDOWS_BACKEND_RELEASE_REPOSITORY`
- `PHANTOM_WINDOWS_BACKEND_RELEASE_TAG`
- `PHANTOM_WINDOWS_BACKEND_TRUST_FORWARDED_HEADERS=true`
- `PHANTOM_BOOTSTRAP_ADMIN_EMAIL` (initial account creation only)
- `PHANTOM_BOOTSTRAP_ADMIN_PASSWORD` (initial account creation only; remove after bootstrap)
- `PHANTOM_BOOTSTRAP_ADMIN_DISPLAY_NAME` (initial account creation only)
- `PHANTOM_WINDOWS_BACKEND_GOOGLE_OAUTH_CLIENT_SECRETS_JSON`
- `PHANTOM_WINDOWS_BACKEND_GOOGLE_OAUTH_REFRESH_TOKEN` (required for a cold deployment without an existing encrypted token)
- `PHANTOM_WINDOWS_BACKEND_RAZORPAY_KEY_ID`
- `PHANTOM_WINDOWS_BACKEND_RAZORPAY_KEY_SECRET`
- `PHANTOM_WINDOWS_BACKEND_RAZORPAY_WEBHOOK_SECRET`

Dashboard backend:

- `PHANTOM_DASHBOARD_BACKEND_DATABASE_URL`
- `PHANTOM_WINDOWS_BACKEND_INTERNAL_API_KEY`
- `PHANTOM_DASHBOARD_BACKEND_TRUST_FORWARDED_HEADERS=true`

## Notes

- SMTP on Render Free is a bad fit because Render documents that Free web services cannot send outbound traffic on ports `25`, `465`, or `587`.
- This setup keeps Gmail API mail delivery, not SMTP.
- Both deployed backends run with `ASPNETCORE_ENVIRONMENT=Production`.
- Phone verification is disabled by default through Admin → Settings. Before enabling it, configure a production OTP provider and its credentials; mock OTP remains local-development-only.
- Production startup fails when security-critical configuration is missing or unsafe. This is intentional: fix the listed environment errors instead of bypassing validation.
- Forwarded headers are trusted with a one-proxy limit because Render terminates public traffic before the service. Do not enable the setting when exposing either Kestrel service directly to the internet.
- Legacy admin API-key variables are no longer used by browser or backend admin routes. Admin access requires the password-plus-email-OTP session; dashboard-to-authority validation uses the internal API key.
