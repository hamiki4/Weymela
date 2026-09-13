# Role and permission matrix

| Capability | Platform Admin | Business | Creator | Customer | Cashier |
|---|---|---|---|---|---|
| Financial pricing/configuration | manage/version/audit | read effective public pricing | read effective earnings | read effective cashback | no access |
| Wallet/deposits | review/approve/audit | submit positive deposits/read own wallet | no Business wallet | own cashback only | no access |
| Promotions | full oversight | create, fund, publish, pause/complete per policy | discover/request/join; produce content | discover Hybrid only | scan eligible Hybrid QR |
| Creator approval/allocation | oversee | approve/reject and assign bounded allocation | view own allocation | no access | no access |
| Financial events | reconcile/settle | see own Promotion summaries | own earnings/payouts | own cashback/payouts | transaction entry only |
| Private contact data | least privilege/audited | own data; no creator private contacts before approval | own data; no owner private contacts | own data | assigned operational data |

Every endpoint enforces role and resource ownership server-side. Terms acceptance stores document type, version, actor, timestamp, and consent evidence for Business, Creator, and anti-circumvention terms.

Phase 4 has application/persistence services only, not HTTP endpoints. A future host derives Actor from verified authentication, never a request-supplied role/subject. One verified identity may have multiple active CommercePermissions (for example Customer, Creator and one or more Business memberships); the session must carry one explicitly selected profile and every request rechecks that membership and resource ownership. CommercePermissions is a fail-closed local authorization seam: Customer/Creator operations verify active user-to-subject membership; Cashier and Business owner/admin require active Business-specific checkout grants. Only Platform Admin can mark external payouts paid, settle revenue or read full Admin financial projections. Repositories and internal posting services are not public transport boundaries.

Offer token handling is specified in [Phase 4](../finance/PHASE-4-FINANCIAL-ENGINE.md): hash-only persistence, no raw-token logs or URL parameters, explicit one-time issuance response, redacted diagnostic/JSON representations, and safe offer projections. No contact information is exposed by the new public-identity contracts. Current Creator legal versions are gated at go-live.

Additional profile onboarding is a restricted authenticated-account capability. Public choices are Customer, Creator, and Business; Cashier and PlatformAdmin remain trusted provisioning paths. Customer activation is immediate after required account/terms checks; Creator and Business remain approval-gated. `RoleEnrollment` is request history, while only an approved active `CommercePermission` grants workspace access. Pending or rejected requests do not affect existing profiles, and a zero-profile account can see only its own onboarding status and account/legal controls.
