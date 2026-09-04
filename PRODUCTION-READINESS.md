# Phantom Production Readiness

## Required Configuration

Configure these values in the Windows authority backend before deploying the new frontend:

- `PHANTOM_WINDOWS_BACKEND_DATABASE_URL`
- `PHANTOM_WINDOWS_BACKEND_INTERNAL_API_KEY`
- `PHANTOM_WINDOWS_BACKEND_SECRET_ENCRYPTION_KEY`
- `PHANTOM_WINDOWS_BACKEND_DOWNLOAD_SIGNING_KEY` with an independent high-entropy secret
- `PHANTOM_WINDOWS_BACKEND_RELEASE_REPOSITORY`
- `PHANTOM_WINDOWS_BACKEND_RELEASE_TAG`
- `PHANTOM_WINDOWS_BACKEND_TRUST_FORWARDED_HEADERS=true` on Render
- `PHANTOM_DASHBOARD_BACKEND_TRUST_FORWARDED_HEADERS=true` on Render
- `PHANTOM_PUBLIC_WEBSITE_BASE_URL`
- Gmail OAuth client settings plus either an existing encrypted refresh token or `PHANTOM_WINDOWS_BACKEND_GOOGLE_OAUTH_REFRESH_TOKEN`, or SMTP settings that can deliver mail to every administrator
- production Razorpay keys and webhook secret

Use the Razorpay API key secret exactly as issued. Do not pad or transform it. Generate the separate webhook secret with at least 32 characters and configure the identical value in Razorpay and Render.

Keep the bootstrap admin password and all provider, payment, mail, database, and signing secrets in the deployment secret manager. Do not place them in Vite variables or source control.

Both backends now fail fast when production starts with missing database/inter-service secrets, non-HTTPS public origins, unsafe signing/payment configuration, unavailable email delivery, test-user seeding, local admin bootstrap, or untrusted proxy handling. Phone verification can remain disabled without an SMS provider; the admin API refuses to enable it until production OTP credentials exist.

## Safe Rollout Order

1. Back up the production PostgreSQL database.
2. Confirm Gmail OAuth or SMTP delivery with the existing admin email. Admin password login now requires the emailed OTP.
3. Deploy `phantom-windows-app-backend`. Its migrations create admin OTP challenges, the admin action audit trail, and the email-verification dashboard projection.
4. If a separate dashboard replica database is configured, wait until the projection outbox is empty and confirm `email_verified` is populated in `dashboard_account_summaries`.
5. Deploy `phantom-dashboard-backend`. Its migration adds the compatible email-verification read-model column.
6. Deploy `phantom-website-dashboard`.
7. Rotate any test credentials that were shared during pre-production review.

Do not deploy the frontend first: it no longer stores bearer tokens and expects the updated cookie, OTP, signed-download, audit, payment-filter, and device-revocation contracts.

## Post-Deploy Smoke Test

Start with the automated public boundary checks:

```bash
PHANTOM_SMOKE_WEBSITE_URL=https://your-site.example \
PHANTOM_SMOKE_WINDOWS_BACKEND_URL=https://your-authority.example \
PHANTOM_SMOKE_DASHBOARD_BACKEND_URL=https://your-dashboard-api.example \
npm --prefix phantom-website-dashboard run smoke:production
```

Then complete the authenticated checks below because admin email OTP, payments, and account-specific state require controlled test accounts.

- Register a new user with phone verification both enabled and disabled; verify the copy and required fields match the setting.
- Verify the email, sign in, refresh the browser, and confirm the user session restores without tokens in local storage or JSON responses.
- Request Windows and macOS downloads; confirm the links expire and a locked or unverified account cannot create or redeem one.
- Revoke a secondary device and the current browser device; confirm the latter returns to login.
- Complete admin password plus email OTP login, including invalid, expired, and reused codes.
- Exercise one credit grant, account lock, lock clear, debt waiver, support update, and managed-credential change; confirm each appears in the Audit page.
- Confirm a non-`super_admin` account cannot perform privileged mutations and a `support_admin` is limited to support and interview-lock operations.
- Search and filter payment orders across more than one page and confirm totals remain stable.
- Test the product site, user dashboard, and admin dashboard at 375 px, 768 px, and desktop widths with keyboard-only navigation.
- Confirm `/health/ready` succeeds for both backends and that Razorpay webhook processing, mail delivery, projection lag, and error rates are monitored.
- Confirm the CI workflow passes for the deployed commit and Dependabot is enabled for the repository.

## GitHub Release Boundary

The website no longer contains direct release URLs. It requests a short-lived, HMAC-signed, account-bound URL from the authority backend, which rechecks email verification and account-lock state before redirecting to the configured GitHub release asset.

GitHub public releases are still discoverable through GitHub itself, and the redirect target is visible to the downloading client. This provides gated application flow, not asset confidentiality. Moving to private object storage later only requires replacing the final asset-resolution step in `DownloadLinkService`; the frontend contract can remain unchanged.
