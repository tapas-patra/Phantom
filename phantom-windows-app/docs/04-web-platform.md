# Phantom Web Platform

## 1. Goal

Phantom needs two web surfaces in addition to the Windows app:
- a public marketing website for acquisition and conversion
- an authenticated user dashboard for billing, credits, devices, downloads, and usage visibility

These surfaces should share the same backend authority as the desktop app:
- auth
- wallet and ledger
- entitlements
- payments
- device state
- hosted knowledge base metadata

The website and dashboard should not be embedded into the WPF app codebase. They should be separate deployable apps that talk to the same backend.

## 2. Recommended Stack

Recommended web stack:
- `Next.js` with the App Router
- `TypeScript`
- `Tailwind CSS`
- `Supabase Auth`
- `Supabase Postgres`
- `Razorpay`
- deployment on `Vercel` or equivalent

Why:
- Next.js App Router is the current mainstream production path for server-rendered React applications and supports layouts, nested routes, server/client components, and server-side endpoints. [Next.js App Router docs](https://nextjs.org/docs/app)
- Supabase documents a supported Next.js App Router auth flow and session handling model. [Supabase Next.js auth quickstart](https://supabase.com/docs/guides/auth/quickstarts/nextjs)
- Razorpay supports subscriptions and API-driven billing management from a dashboard/backend flow. [Razorpay subscriptions docs](https://razorpay.com/docs/payments/subscriptions/?locale=en-US)
- For Phantom launch billing, use one-time credit-pack purchases, not recurring subscriptions.

## 3. Product Split

### Public Website
Purpose:
- explain Phantom
- compare Free Trial, Pro BYO, and Premium AI
- show trust and product positioning
- drive sign-up and purchase intent
- host docs, FAQs, and download links

Suggested routes:
- `/`
- `/pricing`
- `/features`
- `/compare/parakeet`
- `/faq`
- `/download`
- `/register`
- `/legal/privacy`
- `/legal/terms`

### User Dashboard
Purpose:
- manage account
- buy credits
- view usage and wallet balance
- manage devices
- download desktop app
- resolve negative balance
- manage hosted knowledge base

Suggested routes:
- `/app`
- `/app/wallet`
- `/app/billing`
- `/app/usage`
- `/app/devices`
- `/app/downloads`
- `/app/knowledge`
- `/app/settings`

## 4. Architecture Split

Use one shared backend domain model, but two clients:
- desktop client
- web client

Both talk to the same hosted services for:
- authentication
- ledger and wallet
- entitlements
- device registry
- hosted knowledge base

Do not duplicate business rules across web and desktop. The backend should be authoritative for:
- trial eligibility
- credit balance
- negative balance
- payment success/failure
- plan entitlements

## 5. Web Component Architecture

### Marketing App
Keep this mostly static and SEO-oriented.

Recommended page composition:
- hero and product narrative
- feature sections
- plan comparison
- testimonials / proof points later
- FAQ
- CTA to sign up and download

Data needs:
- mostly static content
- pricing config fetched from backend or environment-backed config

### Dashboard App
Keep this server-authenticated and data-backed.

Dashboard modules:
- `Account`
- `Wallet`
- `Billing`
- `Usage`
- `Devices`
- `Downloads`
- `Knowledge`
- `Support`

## 6. Dashboard Feature Set

### Account
- profile details
- email and phone verification state
- plan and entitlement summary

### Wallet
- Pro app-usage credits
- Premium managed-AI credits
- Premium negative balance
- last usage date
- current plan
- buy-credit CTA

### Billing
- payment history
- invoices or receipts
- pack purchase history
- refund/support status

### Usage
- session history
- credit burn history
- Premium continuation usage
- BYO emergency takeover usage

### Devices
- active devices
- first seen / last active
- revoke device
- suspicious device warnings
- current interview lock if any

### Downloads
- latest Windows build
- release notes
- system requirements
- install instructions

### Knowledge
- hosted knowledge bases
- document count and storage used
- indexing state
- managed KB creation status

## 7. Backend API Boundaries

The desktop app and web dashboard should both consume a small set of stable backend service surfaces:

### Auth
- sign in
- sign out
- refresh session
- verify phone/email

### Wallet
- get wallet snapshot
- get ledger history
- get negative balance state

### Billing
- create purchase intent
- verify payment webhook result
- fetch payment history

### Usage
- list interview sessions
- list usage blocks
- list continuation events

### Devices
- register device
- list devices
- revoke device

### Knowledge
- create KB
- upload docs
- list docs
- trigger reindex

## 8. Shared Data Model

The web dashboard should read from the same hosted schema already planned for the desktop architecture:
- `user_profiles`
- `devices`
- `wallets`
- `ledger_entries`
- `payments`
- `entitlements`
- `interview_usage`
- `knowledge_bases`
- `kb_documents`
- `risk_flags`

Additional web-only supporting tables may be useful:

### `download_releases`
- version
- platform
- changelog
- download_url
- is_latest

### `support_tickets`
- user_id
- category
- status
- subject
- created_at

## 9. Auth Flow

Recommended flow:
1. desktop app `Register` opens `/register?source=desktop` and includes desktop app version and device metadata
2. user signs up on website or dashboard
3. Supabase Auth creates identity
4. phone OTP is completed during registration
5. website completes a magic-link return to `phantom://auth/callback`
6. desktop app stores the authenticated desktop session securely with DPAPI-backed protection
7. dashboard reads wallet and entitlement state from backend
8. desktop app uses the same account and fetches the same entitlement state

Important:
- web session is cookie-based
- desktop session is token-based and stored securely with DPAPI
- both session types map to the same user identity
- desktop login should also support in-app `email/password` and `magic link` flows

## 10. Purchase Flow

Recommended flow:
1. user clicks buy credits in dashboard or website
2. backend creates Razorpay order or payment link
3. user completes payment
4. Razorpay webhook updates `payments` and posts `ledger_entries`
5. the correct wallet balance type updates server-side
6. dashboard reflects new balance
7. desktop app picks up updated entitlement on next sync

Do not let the web client mutate wallet state directly.
Do not implement recurring billing first; launch with one-time pack purchases only.

## 11. Usage Visibility

Usage page should show:
- session date/time
- plan used
- total minutes
- credits burned
- continuation or overage used
- mode: Free Trial / Pro BYO / Premium
- balance type affected: Pro / Premium

This matters because Phantom has a credit-based model and users will challenge unexplained credit burn.

## 12. Design Direction

The public website and dashboard should feel like one product, but they serve different jobs:

### Website
- high trust
- crisp explanation
- strong pricing comparison
- minimal friction

### Dashboard
- operational clarity
- fast access to credit state
- no marketing noise
- user should immediately understand:
  - what plan they are on
  - how many credits remain
  - whether any negative balance blocks next interview

## 13. Repo Strategy

Do not place the website inside the current WPF project.

Recommended structure:

```text
/Phantom
  /desktop
    current WPF app
  /web
    Next.js marketing site + dashboard
  /backend
    optional backend functions, migrations, shared contracts
```

If you do not want a monorepo immediately, keep:
- current repo for desktop
- separate repo for web/dashboard

But keep shared contract docs for:
- plan codes
- wallet semantics
- ledger entry types
- entitlement flags

## 14. Implementation Phases

### Phase A
- build public website
- launch pricing, features, FAQ, and download pages

### Phase B
- build authenticated dashboard shell
- add wallet, billing, and downloads views

### Phase C
- connect Razorpay purchase flow
- connect usage history and ledger views

### Phase D
- add devices management
- add hosted knowledge management
- add support and account workflows

## 15. Immediate Recommendation

Build the web platform in this order:
1. public website
2. dashboard auth
3. wallet and purchase flow
4. usage history
5. devices
6. hosted knowledge management

This keeps go-to-market and monetization moving before the heavier Premium knowledge workflows land.
