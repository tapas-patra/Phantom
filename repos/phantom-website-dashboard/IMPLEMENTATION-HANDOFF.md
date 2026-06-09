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
