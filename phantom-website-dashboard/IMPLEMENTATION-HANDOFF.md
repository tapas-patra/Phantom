# Website And Dashboard Frontend Handoff

This repo will own all user-facing web surfaces.

## Frontend Areas

### Website
- marketing landing page
- product explanation
- pricing
- desktop download page

### Auth
- register flow
- login flow
- magic-link UX
- desktop callback handoff UX

### Dashboard
- wallet balance view
- credit-pack purchase history
- device management UI
- app download access
- support/admin entry surfaces as allowed

## Required Integrations

- `phantom-windows-app-backend` for auth/startup/device/desktop-linked behavior
- `phantom-dashboard-backend` for dashboard queries and history views

## Current Desktop Expectations

The Windows app already assumes:
- first open offers `Login` or `Register`
- `Register` opens the website
- magic-link callback returns control to the desktop app
- app version and device metadata are passed to the register route

## First UI Build Order

1. registration entrypoint
2. login entrypoint
3. app download page
4. dashboard shell
5. wallet/devices/history pages

## Production Browser Contract

- Browser authentication is cookie-only. Access and refresh tokens are stored in `HttpOnly`, `Secure`, `SameSite=Strict` cookies and must not be returned to or persisted by frontend JavaScript.
- Every unsafe browser API request sends `X-Phantom-CSRF: 1`; both backends reject cross-origin unsafe requests without it.
- Admin login is two-step: `POST /api/admin/auth/login` validates the password and emails a six-digit OTP, then `POST /api/admin/auth/verify-otp` completes the session.
- Installer buttons call `POST /api/desktop/downloads/signed-url`. The returned URL expires quickly and resolves through the authority backend to the configured GitHub release asset.
- Device sign-out calls `POST /api/desktop/sessions/revoke-device`; the authority backend revokes matching active sessions and refreshes dashboard projections.
- Admin payment search and status filters are server-side so pagination totals match the full result set.
- Safe reads retry one transient failure; dashboard sections expose explicit loading and retry states without retrying mutations.
- Knowledge-base editing uses focused editor sections so profile, documents, experiences, and projects are not presented as one oversized form.
- Shared dashboard primitives live under `src/components/`; pure formatting and payment-state helpers live under `src/lib/format.js` and are covered by Node tests.

Do not reintroduce direct GitHub release URLs or browser-readable bearer tokens in the frontend.
