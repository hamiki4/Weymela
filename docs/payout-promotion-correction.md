# Payout destinations and promotion funding

Customer and Creator mobile-money destinations accept an independently entered Ethiopian receiving number and recipient name. Saving changes only the encrypted destination and its audit event. It never verifies a recipient, updates an identity phone or posts money. Existing native payloads without the new fields preserve the saved destination. Provider, recipient, Admin authority and available-balance checks remain required before recording a payout.

Promotion accounting continues to use each agreement's saved financial version:

| Model | Content/view payment | QR purchase |
| --- | --- | --- |
| UGC Only | One fixed content payment after approval, publication verification and Go Live; no view threshold | No QR offer |
| UGC + Sale | Same fixed content payment | Separate cashback reserve and saved Platform sale fee; no repeated Creator content payment |
| View Only | New provider-verified view blocks at saved pricing | No QR offer |
| View + Sale | New provider-verified view blocks at saved pricing | Attributed Creator commission, Customer cashback and Platform share drawn from the funded Creator allocation |

UGC + Sale now has a separate Add Funds action. It requires an active Business, ownership, an unexpired active/exhausted offer, a positive amount, available wallet funds, the offer version and an idempotency key. It preserves the content obligation and saved cashback/pricing terms. A balanced reserve journal and append-only budget movement accompany each increase. Business alerts indicate low funds at 20% remaining (also when cashback cannot cover another purchase of the same size) and exhausted funds. View/sale allocation alerts and existing replenishment remain separate.

Migration `20261016161806_AllowFundedCashbackReplenishment` replaces two existing trigger functions. It permits only fully reserved increases, preserves immutable ownership/rates/consumption and reconciles each increase against immutable original reservations, budget entries and journals. It does not update existing rows or create balances. Application rollback uses the prior images with this compatible forward migration retained. Database downgrade refuses when top-up history exists; never remove financial history to force downgrade.

Repeated instructional text moves into role-specific Settings → Help, with English and Amharic text. Required settlement, payout, deletion and publication confirmations remain. UGC cards no longer label the agreed fee as a remaining payment. Cashback availability is shown in its own budget section.

Release gates: targeted database/API/browser tests; required CI and source/dependency/image security checks; backup and isolated restoration/migration rehearsal; Pilot validation before protected main merge; identical verified source deployed to Production with ledger and identity/configuration preservation checks. The Android release uses the verified Production UI, original upload key and a newly checked unused versionCode.

The explicit operator allowlist now supports ViewRewards. Enabling this operation still requires genuine provider ownership and fresh count evidence at the service boundary. Missing provider integration returns ProviderUnavailable and never posts a reward. Unknown money operations remain denied.

Provider fixtures demonstrate software behavior only. They do not establish real TikTok views, external ownership, settlement or payouts. Missing provider credentials or required product-specific Admin values must be reported explicitly; never invent those values or disable unrelated operations.
