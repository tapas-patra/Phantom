# Vercel Deployment

This repo is prepared for three separate Vercel projects with these root directories:

- `phantom-windows-app-backend/`
- `phantom-dashboard-backend/`
- `phantom-website-dashboard/`

Current hosted URLs:

- Windows backend: `https://phantom-ai-windows-app-backend.vercel.app`
- Dashboard backend: `https://phantom-dashboard-backend.vercel.app`
- Website: `https://phantom-website-dashboard.vercel.app`

## Project 1: `phantom-windows-app-backend`

Required:

- `PHANTOM_WINDOWS_BACKEND_DATABASE_URL`
- `PHANTOM_WINDOWS_BACKEND_INTERNAL_API_KEY`
- `PHANTOM_WINDOWS_BACKEND_SECRET_ENCRYPTION_KEY`

Set for this deployment:

- `PHANTOM_PUBLIC_WEBSITE_BASE_URL=https://phantom-website-dashboard.vercel.app`

Usually needed in production:

- `PHANTOM_WINDOWS_BACKEND_ADMIN_API_KEY`
- `PHANTOM_BOOTSTRAP_ADMIN_EMAIL`
- `PHANTOM_BOOTSTRAP_ADMIN_PASSWORD`
- `PHANTOM_BOOTSTRAP_ADMIN_DISPLAY_NAME`
- `PHANTOM_WINDOWS_BACKEND_SMTP_HOST`
- `PHANTOM_WINDOWS_BACKEND_SMTP_PORT`
- `PHANTOM_WINDOWS_BACKEND_SMTP_USERNAME`
- `PHANTOM_WINDOWS_BACKEND_SMTP_PASSWORD`
- `PHANTOM_WINDOWS_BACKEND_SMTP_FROM_EMAIL`
- `PHANTOM_WINDOWS_BACKEND_SMTP_FROM_NAME`
- `PHANTOM_WINDOWS_BACKEND_SMTP_ENABLE_SSL`
- `PHANTOM_WINDOWS_BACKEND_GOOGLE_OAUTH_CLIENT_SECRETS_JSON`
- `PHANTOM_WINDOWS_BACKEND_GOOGLE_OAUTH_REDIRECT_URI=https://phantom-ai-windows-app-backend.vercel.app/api/admin/integrations/gmail/oauth/callback`
- `PHANTOM_WINDOWS_BACKEND_RAZORPAY_KEY_ID`
- `PHANTOM_WINDOWS_BACKEND_RAZORPAY_KEY_SECRET`
- `PHANTOM_WINDOWS_BACKEND_RAZORPAY_WEBHOOK_SECRET`
- `PHANTOM_WINDOWS_BACKEND_OTP_PROVIDER`
- `PHANTOM_WINDOWS_BACKEND_OTP_API_KEY`
- `PHANTOM_WINDOWS_BACKEND_OTP_SENDER_ID`
- `PHANTOM_WINDOWS_BACKEND_OTP_TEMPLATE_NAME`
- `PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_ENABLED`
- `PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_PROVIDER`
- `PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_BASE_URL`
- `PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_API_KEY`
- `PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_MODEL`
- `PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_DIMENSIONS`
- `PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_VERSION`
- `PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_BATCH_SIZE`

Keep disabled unless you explicitly need them:

- `PHANTOM_WINDOWS_BACKEND_ALLOW_IMPLICIT_LOCAL_ADMIN_BOOTSTRAP=false`
- `PHANTOM_WINDOWS_BACKEND_ALLOW_TEST_USER_SEEDING=false`

## Project 2: `phantom-dashboard-backend`

Required:

- `PHANTOM_DASHBOARD_BACKEND_DATABASE_URL`
- `PHANTOM_WINDOWS_BACKEND_INTERNAL_API_KEY`

Set for this deployment:

- `PHANTOM_WINDOWS_BACKEND_BASE_URL=https://phantom-ai-windows-app-backend.vercel.app`
- `PHANTOM_PUBLIC_WEBSITE_BASE_URL=https://phantom-website-dashboard.vercel.app`

Optional but recommended:

- `PHANTOM_DASHBOARD_ADMIN_API_KEY`

## Project 3: `phantom-website-dashboard`

Optional for this exact deployment because the app now auto-falls back to the hosted Vercel URLs outside localhost:

- `VITE_PHANTOM_WINDOWS_BACKEND_API_BASE_URL=https://phantom-ai-windows-app-backend.vercel.app`
- `VITE_PHANTOM_DASHBOARD_API_BASE_URL=https://phantom-dashboard-backend.vercel.app`

Set them anyway if you want the values visible in project settings or if you later switch to custom domains.

## Windows app

The Windows app is not hosted on Vercel. It now ships with a checked-in [phantom.hosted.json](/Users/tapaskumarpatra/TKP-Other-personal/Phantom/phantom-windows-app/phantom.hosted.json:1) that is copied into the build output, so users do not need to set env vars manually for the current deployment.

Override only if you move off these domains:

- `PHANTOM_WINDOWS_BACKEND_BASE_URL`
- `PHANTOM_WEBSITE_BASE_URL`
- `PHANTOM_HOSTED_MODE=remote`
