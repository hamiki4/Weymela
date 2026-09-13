# Phase 5 role workspaces and HTTP boundary

This implements the approved Phase 5 scope against the existing V3 schema. [UX-STANDARDS.md](../product/UX-STANDARDS.md) remains authoritative. Internal Promotion/CreatorAllocation names remain unchanged; screens use Campaign, Creator Budget, Your Budget and Available Campaign Budget.

## Structure

- `Weymela.Web`: React 19, TypeScript, Vite; feature folders for Business, Creator, Admin and commerce; shared semantic controls, responsive cards/tables, role navigation and request state handling. Node 24 is required by the scanner package. Versions and lockfile are checked in.
- `Weymela.Api`: thin role-grouped endpoints, signed HttpOnly/SameSite cookies, active-account authorization policies, same-origin mutation header, privacy-safe errors and explicit response projections.
- `Weymela.Application/Web`: transport-independent input/output contracts and a trusted public-directory seam. No private authentication/contact records are copied into these projections.
- `Weymela.Infrastructure/Web`: queries and command orchestration over the approved application/financial services. Financial calculations, reservations, view rewards, checkout and payouts still use the Phase 2–4 engine.
- `Weymela.BrowserHost`: test-only executable. Creates its own loopback PostgreSQL Testcontainer and random `v3_test_*` database, migrates that database, seeds through real commands and serves the production frontend/API on one ephemeral loopback origin. It does not accept an external database connection.

## HTTP surface

All paths below are under `/api`. Role groups require both authenticated role and an active persisted workspace permission. Business and Creator ownership is rechecked in application/query boundaries. Extra JSON fields are rejected, including attempted rate or identity overrides.

| Workspace | Reads | Mutations |
|---|---|---|
| Session | `/auth/mode`, `/session` | `/session/switch-profile`, `/session/sign-out`; `/development/session` only in explicitly enabled Development |
| Business | `/business/home`, `/wallet`, `/pricing`, `/campaigns`, `/campaigns/{id}` | `/wallet/deposits` (test funding only), `/campaigns`, `/campaigns/{id}/fund`, `/publish`, `/start`; `/applicants/{id}/approve`, `/reject`; `/creator-budgets/{id}/increase` |
| Creator | `/creator/home`, `/pricing`, `/discover`, `/discover/{id}`, `/campaigns`, `/requests`, `/earnings` | `/campaigns/{id}/join`; `/creator-budgets/{id}/content`; `/participations/{id}/refresh`; `/payouts/request` |
| Admin | `/admin/home`, `/businesses`, `/creators`, `/campaigns`, `/campaigns/{id}`, `/financial-settings`, `/payouts`, `/platform`, `/notifications`, `/audit` | `/financial-settings`; `/payouts/{kind}/{subject}/prepare`, `/payouts/{id}/paid`; `/platform/settlements` |
| Customer | `/customer/offers`, `/history`, `/qr/{id}` | `/offers/{creatorBudgetId}/qr` |
| Checkout | Safe offer resolution is POST `/checkout/resolve`, never a token-bearing URL | `/checkout/confirm` for assigned Cashier or checkout-permitted Business Owner |

Paths abbreviated after the first column entry retain their role prefix. Identity subjects and financial split amounts are never accepted from the browser. Every mutation supplies `X-Weymela-Request: 1`; idempotent operations additionally use `Idempotency-Key`. No cross-origin CORS grant is installed. Cookie identity can later be issued through a trusted Firebase adapter, without changing application authorization.

## Business and Creator workflow

1. The Business creates a draft through a guided Campaign / Creators / Budget form. Campaign type and total budget belong only to Business. Eligibility uses the current supported single category, region and minimum verified-follower criteria; no schema expansion was needed.
2. The draft snapshots authoritative Admin pricing. Funding uses existing current-minimum and wallet checks. Confirmation shows exact reservation and resulting Available / Reserved balances.
3. Publishing a Campaign whose start time has arrived also activates it in the same transaction. Future-start Campaigns remain Published and discoverable; Business explicitly starts them when due. No automatic scheduling worker was added.
4. Creator discovery and joining both evaluate active account, actual funding, remaining budget and trusted directory metrics. The browser cannot claim follower counts or act as another Creator. Requests have their own history even when no longer discoverable.
5. Approve & Set Budget uses one transaction for review, budget assignment, journal, idempotency, audit and outbox. Over-budget assignment rolls back approval. Approval and rejection use Serializable transactions so competing reviews cannot leave a rejected application with an assigned budget. Rejection has idempotent HTTP retries.
6. Approved Creators see only their own budget, earnings and content workspace. Connecting permitted content invokes the existing baseline/go-live service; Refresh Views invokes the existing verification engine. Active top-ups use only unassigned Campaign reserve. No reduction, End Campaign, refund or new finance engine is exposed.

## Financial settings, payouts and privacy

Admin sees current effective settings and every saved version, including stored scheduled values. Optimistic expected-version and idempotency checks protect settings changes. Future versions do not apply early; existing Campaign snapshots do not change.

Business pricing is one table with its view charge and total sale cost, never the internal split. Creator How You Earn is one table with only Creator earnings and **Minimum to cash out**. Admin may see the complete Campaign attribution and settlement history.

Creator earnings and threshold payout history use one existing account. Admin payout tabs are Creators / Customers / Platform / History. Mark Paid and settlement actions require a reference and explicit confirmation of an already completed external payment. No external payment is sent. Partial Platform settlement and payout carry-forward use existing Phase 4 accounting.

Customer discovery is Hybrid-only. Offer QR contains only the opaque token; token hashes remain the only persisted token material. The browser holds the token only in memory long enough to render or redeem it, does not place it in URLs/storage/logs, clears it after checkout, and masks the paste-code field. The safe checkout projection includes public identities, not split values or private contact details. First checkout response and idempotent replay both project the committed sale, preserving PostgreSQL precision consistently.

## Development boundaries and deliberately deferred connections

- Test personas require **all** of Development environment, explicit enable flag, a 32+ character random access key and an isolated development/test database name. Non-development has no test-persona route. Unknown public identities fail closed.
- Fixture seed is **not** called by API startup. Only test hosts seed disposable `v3_test_*` databases. No deployed database is auto-migrated.
- Add Funds accepts any positive amount in the isolated development workspace and clearly identifies test funding. Non-development credit is unavailable until a trusted deposit-confirmation adapter is approved. A Business browser must never be allowed to mint real funds.
- Social verification is a test-provider adapter, not TikTok/YouTube/Instagram integration. Production Firebase sign-in is implemented as a fail-closed email-code/custom-token Web adapter; phone remains an unverified secondary identifier and protected deployment configuration remains required.
- Admin Notifications is a read-only persisted outbox activity feed. Opening it does not mutate processing/read state. Real recipient delivery and read-state UX require later authorized scope; no parallel notification engine was introduced.
- Manual Creator/customer lookup was not implemented in Phase 4. Checkout exposes its clearly disabled shell and a usable scanned-code fallback; both camera and pasted QR use the same verified-sale engine.
- PWA manifest and registration are present. Service worker deliberately caches no API, financial, identity, QR or HTML data. There is no offline write queue; mutation attempts while offline receive a clear reconnect message.

## Rendered acceptance

Required widths are 375, 390, 393, 430, 768, 1366×768, 1440×900 and 1920×1080. Mobile uses intentional cards, 2×2 metric groups, labels and dialogs; desktop uses constrained cards and operational tables. Only Admin dense tables may have contained horizontal scrolling. Buttons have stable green/neutral styles, no transform/glow/purple hover, keyboard focus and native disabled/loading states. Field labels are separate from help descriptions.

Browser tests use the **built frontend + real HTTP + real disposable PostgreSQL**, without mocked API responses. Component tests use small contract fixtures only for focused interactions and state tests. QR scanner testing feeds a real issued QR image through a synthetic camera stream into the real decoder; physical-device camera behavior still requires device acceptance later. Evidence is retained under `.artifacts/phase5-screenshots/`.
Session exchange returns the active workspace plus a privacy-safe list of the account's approved profiles. `POST /api/session/switch-profile` accepts only a selected role/subject/business tuple and resolves it against the authenticated account's active `CommercePermission`; it reissues the protected session and records `ProfileSwitched`, without creating permissions. Requests from a stale browser tab carrying an old `X-Weymela-Profile` context are rejected with `409 ProfileContextChanged`.
## Additional profile onboarding

The authenticated `/api/onboarding/status` and `/api/onboarding/profile` routes use the `VerifiedAccount` policy and never require an active workspace. They expose only the caller's own `RoleEnrollment` history and allow Customer, Creator, or Business requests; Cashier and PlatformAdmin are rejected. Platform Admin reviews through `/api/admin/role-enrollments` and the existing Admin authorization boundary. The `RoleEnrollments` table is additive (migration `20260913054814_AddRoleEnrollments`); the API runtime role needs SELECT/INSERT/UPDATE on that table and its migration-created row-version support, while the migrator remains the only DDL owner.
