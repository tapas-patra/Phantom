# Dashboard Backend Domain Handoff

This repo will own dashboard-facing APIs and aggregations.

## Own Here

- account overview queries
- credit history queries
- credit-pack purchase history queries
- device inventory queries
- download entitlement queries
- support/admin dashboard views
- reporting and aggregation endpoints

## Do Not Own Here

- desktop startup account-check authority
- desktop session lock authority
- desktop usage reconciliation authority
- direct ledger mutation rules for live interview sessions

## Expected Upstream Dependencies

- authoritative wallet and entitlement state from `phantom-windows-app-backend`
- payment and billing event streams from future payment backend work
- support/admin visibility requirements from dashboard product work

## First API Groups

1. account summary
2. wallet summary and history
3. device list
4. desktop installer/download entitlement
5. support case and admin inspection surfaces
