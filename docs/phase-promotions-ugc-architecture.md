# Business-led Promotions and UGC architecture

## Decision

V3 is the sole authority for Promotions and UGC. The V2-derived Web SPA is the
product presentation layer and calls V3 through the canonical same-origin
`/api/` reverse-proxy route. Existing V2 `/api/v1/` routes remain authoritative
only for the accepted Customer/Cashier product and the profile data still
maintained by that product.

No Promotion or UGC financial state is persisted in the V2 database. No runtime
reads or writes another service's database.

## Trust boundary

- The browser authenticates and selects a profile in V3 as it does today.
- V3's host-only workspace cookie authorizes `/api/business`, `/api/creator`,
  `/api/admin`, and `/api/workspace` on `pilot.weymela.com`.
- The V2-derived SPA sends credentials to those same-origin routes. It never
  receives integration client credentials.
- V3 checks the active `CommercePermission`, profile subject, Business
  ownership, Creator ownership, and Admin role on every operation.
- Existing `/api/v1/` product-session authorization remains unchanged for the
  accepted V2 Customer/Cashier paths.

## Promotion reuse and additive changes

The existing V3 `Promotion`, `CreatorApplication`, `CreatorAllocation`,
`BusinessWallet`, pricing snapshot, journal, idempotency, audit, and notification
outbox types remain authoritative.

Additive Promotion data provides:

- optional slogan, location, and safe resource URLs;
- selected social platforms and capacity per platform;
- a stable Creator social-profile reference on each request/allocation;
- platform-capacity concurrency enforcement;
- non-material descriptive revisions without changing pricing snapshots,
  allocations, platform selection, capacity below approvals, or dates beneath
  approved work.

Existing domain statuses map to product wording as follows:

| Domain | Product |
| --- | --- |
| Draft | Draft |
| Funded | Draft |
| Published | Open |
| Active | Active |
| BudgetExhausted | Ended |
| Completed | Completed |
| Cancelled | Cancelled |

## Creator social-profile projection

V3 stores stable, non-secret Creator social-profile projections keyed by the V3
Creator subject. They contain platform, normalized profile URL, self-reported
audience count, and verification status. The existing profile integration
synchronizes these values; authentication credentials remain exclusively in V3
identity services. Promotion requests reference the stable projection ID rather
than free-text platform names.

## UGC authority

UGC lives in V3 because it must share the authoritative Business wallet,
financial-configuration version, Creator and Business subjects, Admin roles,
idempotency store, journals, earnings account, platform revenue, audit, and
notifications.

UGC-specific aggregates are limited to:

- opportunity and immutable requirement revisions;
- safe URL resources;
- Creator request;
- approved assignment and revision acceptance;
- deliverable submission/review;
- reservation and budget movement records.

Publishing reserves `creator payment * capacity + platform fee` atomically.
Deliverable approval consumes the assignment's Creator payment and proportional
platform fee once, creates one Creator earning and one Platform revenue entry,
and is protected by serializable execution plus an idempotency record.

Published UGC with approved assignments cannot be cancelled in this phase. That
is the safest non-destructive policy until compensation terms are approved.

## Financial configuration

`FinancialConfigurationVersion` is extended additively with a UGC settings
snapshot: minimum Creator payment, platform fee percent, and optional minimum
UGC budget. Existing View Only, View & Sale, Sale split, and payout values remain
unchanged and historical versions remain readable.

## Admin roles

V3 gains `OperationsAdmin` as an active workspace role. Platform Admin remains
the only role allowed to grant/revoke Admin roles or create financial-setting
versions. Operations Admin receives explicit read/process policies only. The
ongoing grant flow targets an existing verified V3 identity and protects the
last active Platform Admin.

## Migration and release boundary

The expected database change is one additive V3 migration. The V2 database is
not migrated for Promotions or UGC. V3 API and V2 Web are the expected changed
runtime components; V3 Worker changes only if notification/outbox processing
requires a shared-assembly rebuild. Production and the original V2 worktree are
out of scope.
