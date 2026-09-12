# Phase 4 financial engine and checkout

This document implements the locked [Product Spec](../product/WEYMELA-V3-PRODUCT-SPEC.md). Internal names remain Promotion/CreatorAllocation; future UI uses Campaign/Creator Budget under [UX standards](../product/UX-STANDARDS.md). No Web pages, HTTP checkout endpoints, live social adapter, payment provider, broker or deployment is included.

## One attributable spending path

`VerifiedViewService` and `CheckoutService` both call `AttributableFinance.PostAsync`. Every Business-side expense consumes the bound CreatorAllocation, the Campaign reserve and the Business wallet Reserved balance together. Available is never a fallback; another Creator Budget is never a fallback. Creator, Customer and Platform credits all reference the same authoritative journal. WalletEntry/PromotionBudgetEntry remain operational journal references, not competing ledgers.

| Operation | Debit | Credit |
| --- | --- | --- |
| Verified views | CreatorAllocatedReserve: Business charge | CreatorPayable + PlatformRevenue |
| Verified Hybrid sale | CreatorAllocatedReserve: complete sale charge | CreatorPayable + CustomerCashbackPayable + PlatformRevenue |
| Confirm Creator payout | CreatorPayable: pinned threshold | CashClearing |
| Confirm Customer payout | CustomerCashbackPayable: pinned threshold | CashClearing |
| Platform settlement | PlatformRevenue: settled amount | CashClearing |

All Phase 4 mutations use PostgreSQL SERIALIZABLE transactions with existing EF Unit of Work, optimistic Version/xmin protection, idempotency, audit and transactional outbox. Serialization/deadlock errors are translated even when Npgsql wraps them in a transient-failure exception. There are no automatic financial retries. A caller reloads and explicitly retries with the original request/key. Failure, including outbox insertion failure, rolls back the entire operation.

## Verified views

The host supplies `IVerifiedViewProvider`; no permissive or live provider is registered. Tests provide verified counts/evidence. The provider input includes Creator, Campaign, provider and external content ID. Output includes count, UTC verification time and evidence reference. Unsupported/mismatched identity, negative counts and future timestamps fail closed.

Go-live requires active Creator membership, active Business, active funded Campaign/Creator Budget and exact current Creator legal acceptances. A separate CreatorPromotionParticipation binds one content item to the approved allocation and captures its baseline once. The content identity, baseline and go-live time cannot be rewritten in SQL. Current implementation permits one content item per allocation and one Campaign attribution per provider/content pair; multiple-content attribution needs an explicit future extension, not a baseline reset.

Campaign verified views = authoritative latest minus baseline. Lower counts or older evidence are stored as immutable anomaly snapshots without decreasing or advancing authoritative latest. Pausing prevents reward processing; resume preserves baseline and rewarded counts. Views accrued while paused remain in the cumulative delta on resume. Completion cannot resume or consume again.

Only complete unpaid blocks qualify. A refresh batches affordable complete blocks into one balanced journal plus a ViewRewardReceipt containing block count and rewarded-through count. No partial block is paid. Example: baseline-adjusted 7,400 views, 3,000 already rewarded, 3,000/block gives one new reward and 1,400 carry-forward views. Complete but unfunded blocks remain unrewarded, separately from the incomplete remainder.

Insufficient allocation for a complete block sets FundingRequired and emits CreatorBudgetExhausted, without crediting that block or advancing its rewarded count. An exactly empty allocation also has FundingRequired state. Campaign exhaustion follows the existing aggregate state machine. Business can top up an exhausted Creator Budget only from available Campaign reserve while that Campaign still permits top-ups. It does not reclaim consumed funds; a subsequent refresh can settle the pending full blocks. Ordinary completion returns unused allocation to Campaign reserve, never Business Available.

## QR and one checkout engine

Customer discovery and QR issuance require active VIEW_PLUS_COMMISSION, active participation, usable Creator Budget, active Business and valid Customer. VIEW_ONLY fails server-side and is absent from commerce projections.

Each OfferQrSession binds Customer, Campaign, Creator, CreatorAllocation and Business. The token is 32 cryptographically random bytes encoded base64url; its payload contains no identity, money or pricing. Only the uppercase SHA-256 hash is persisted. Expiry is exactly five minutes; the exact expiry instant is already invalid. Replacement creates a new token/session and retains history. QR binding, expiry and token hash cannot change; a used QR cannot be reset.

`SensitiveQrToken` redacts ToString and generic JSON serialization. The future issuing HTTP adapter must explicitly return its Value to that Customer once, never log it, never place it in a URL/query string, and never enable request-body/token logging. Issuance replay returns the same session reference without a recoverable token; after a lost issuance response, the client uses a new issuance key to request a replacement. No reversible token storage is introduced to support retries.

Both an assigned Cashier and Business owner/admin with checkout permission use `CheckoutService`: resolve safe offer, enter PurchaseAmount, confirm. A trusted future identity adapter must populate CommercePermissions; role labels alone do not grant checkout. Wrong-Business attempts fail without consuming the QR. Resolve and redeem both revalidate active offer state. Redemption atomically creates the sale, consumes the correct Creator Budget, credits all three shares, posts the journal/audit/outbox/idempotency and marks QR used. Exact valid replay returns the committed sale even after expiry. Changed purchase/key fingerprints conflict; a used QR with a new key cannot create another sale. Concurrent scanners can commit at most one sale.

The future manual resolver returns a trusted CheckoutBinding (party/allocation IDs and source reference only). It must enter the same internal sale transaction/posting path, not accept financial splits or implement a second finance engine. Manual lookup and manual HTTP routes are not implemented in Phase 4.

## Snapshot prices and rounding

Prices come solely from the immutable Campaign Admin snapshot. Future Admin versions affect new quotes/current payout eligibility, not past Campaign prices. View BusinessCharge must equal CreatorEarning + PlatformEarning. Total sale percentage is derived from the three Admin percentages, so there is no competing mutable total. Validation rejects negative rates, totals above 100 and an inconsistent declared total when supplied.

ETB money is persisted at two decimals; each sale beneficiary share is rounded independently to two decimals using midpoint-away-from-zero. The total Campaign charge is the sum of those rounded shares, ensuring exact journal/budget balance. Thus a 1,000 purchase at 4.5% / 2% / 3.5% is 45 / 20 / 35, charge 100. At a 1-unit purchase the same rates round to .05 / .02 / .04, charge .11. Sub-cent purchase inputs and purchases producing zero total payable charge are rejected. This small-amount rounding policy is explicit and tested; owner review is required before real-money checkout, including whether a different residual-allocation rule is desired.

## Payouts and settlement

Creator view rewards and sale commission credit one Creator earnings account across Campaigns. Cashback credits one Customer account, exclusively from eligible verified sales. Business completion cannot reclaim either. Current effective Admin thresholds determine eligibility; no monthly/weekly payout schedule exists.

PreparePayout records one pending candidate per beneficiary, pinning exact threshold, configuration version, amount and eligibility time. Preparation does not claim an external payment happened or deduct/reserve account value. Admin MarkPaid requires an external confirmation reference; only then does the transaction debit available earnings/cashback and post its journal. Available 5,400 / threshold 5,000 pays 5,000 and retains 400. Later threshold changes do not rewrite a prepared candidate. Same-key payment replay is safe; already-paid candidates and duplicate external references cannot pay twice.

Platform settlement uses the existing PlatformRevenueEntry and PlatformSettlement records, never another Platform wallet. Admin may record a positive partial settlement up to Unsettled. Accrued = sum of revenue entries, Settled = sum of settlements, Unsettled = difference. The Admin-only query returns these totals and confirmation history. No external transfer is performed by MarkPaid or settlement recording.

## Role projections

Admin financial detail exposes Campaign assigned/unassigned/used/remaining, each Creator's budget, baseline/latest/Campaign/rewarded views, view earnings, sales, commission, cashback attribution and Platform revenue. Business receives own Campaign/Creator budget summaries and total Business view/sale cost, not the internal split. Creator receives own budget, views and earnings only. Customer receives safe public identities, cashback rate and own purchase/cashback history. Cashier resolves public offer data and receives purchase/total Business charge on completion, not internal splits.

Completed allocations retain original/used/unused values for historical reconciliation, but status makes the unused amount non-spendable by that Creator. Future UI must identify it as returned to Campaign reserve, not new personal earnings or available Business wallet funds.

## Persistence and review

Additive migration: `20260911233032_AddViewRewardsQrAndPayouts`. InitialV3Schema and its integrity helper are unchanged. New tables: CreatorPromotionParticipations, ViewRewardReceipts, OfferQrSessions, PayoutRecords, CommercePermissions. Existing verification snapshots gain reported-count/anomaly/baseline/participation fields; settlements gain SettledBy. Money retains numeric(18,2), percentages numeric(9,4). No existing table or data column is dropped. Down is explicitly disabled: financial history needs a reviewed forward/compensating migration, not schema teardown.

PostgreSQL guards enforce immutable baselines/reward receipts, QR hash/binding/one-use sale, snapshot-derived sale amounts, one-time payout transitions, account earnings less confirmed payouts, and payout journal beneficiary/amount reconciliation. Existing balanced-journal, wallet, allocation, Platform and cashback constraints remain active. Generated forward SQL is in `docs/deployment/AddViewRewardsQrAndPayouts.sql`.

Verification uses disposable PostgreSQL 17 Testcontainers with a separate temporary database per test and loopback-only port. Test-only pooling is disabled to avoid one retained idle connection pool per completed database. Runtime connection pooling is unchanged. No V2/Pilot/Production database/configuration is accessed.

## Owner decisions before live integration

- Confirm small-amount sale rounding policy (independent beneficiary rounding with sum as charge).
- Confirm one bound content item per allocation and exclusive content attribution; any multi-content model needs a separately approved extension.
- Confirm cumulative views during a temporary pause remain eligible after resume; baseline remains immutable either way.
- Choose trusted social verification and identity/checkout permission adapters, plus operational external payment confirmation controls. No live adapter or legal wording is assumed here.

These do not authorize Phase 5 or deployment. The current behavior is explicit, tested and changeable only through a separately reviewed requirement.
