# Weymela V3 Product Specification (authoritative)

The permanent rendered-interface requirements are defined in [UX-STANDARDS.md](UX-STANDARDS.md). That document is normative for all future Web/PWA implementation and visual acceptance.

## Ownership and lifecycle

Weymela is the middleman between Platform Admin, Business, Creator, Customer, and Cashier. A Business owns every Promotion: it funds a wallet, creates and publishes a funded Promotion, defines creator requirements and budget, and approves applicants. Creators discover eligible active Promotions and request to join; they never create Promotions or negotiate platform pricing or the overall budget.

The lifecycle is: wallet funding → draft Promotion → type/budget/requirements → reservation → publish → active → Creator applications → Business approval and per-Creator allocation → verified view/sale events → journaled earnings/cashback/revenue → completion/exhaustion and auditable settlement.

## Financial authority

Platform Admin owns effective-dated, versioned pricing: view thresholds and Business/Creator/Platform amounts for VIEW_ONLY and VIEW_PLUS_COMMISSION; verified-sale Creator, Customer, and Platform percentages; Creator and Customer payout thresholds; and optional minimum Promotion budgets. Business screens never edit or define these values. Transactions snapshot the effective configuration.

V3 removes Business-type minimum-wallet thresholds entirely. Any positive deposit is accepted; only actual Available Balance protects reservations and allocations.

Committed Campaign funding stays reserved on ordinary Campaign/Creator completion. Unused Creator Budget returns to Available Campaign Budget within the Campaign reserve; it does not become Business Available Balance. Normal Business actions cannot reclaim earned funds. Refunds/reversals require a future Platform Admin-authorized exception with compensating journal entries.

Phase 3 persistence and transaction boundaries are specified in [PERSISTENCE.md](../architecture/PERSISTENCE.md). The current ETB schema rejects unsupported currencies or sub-cent persistence rather than silently converting/rounding money. Internal Promotion/CreatorAllocation names continue to map to the role-specific Campaign/Creator Budget terminology in UX-STANDARDS.md.

## Privacy and safety invariants

All mutations are authorized server-side, transactional, idempotent, concurrency-safe, and journaled. Total Balance = Available Balance + Reserved Balance. Reservations and Creator allocations are isolated. VIEW_ONLY never appears in Customer discovery and never creates purchase QR. Contact details remain private until a separately approved, in-platform workflow permits disclosure. Legal acceptance is versioned; no fixed two-year restriction is encoded.

## Phase 4 financial integration

The [Phase 4 financial engine specification](../finance/PHASE-4-FINANCIAL-ENGINE.md) defines the implemented transaction, verification, QR, payout, settlement and privacy contracts. All attributable Business cost—both view charge and complete Hybrid sale charge—consumes the same bound Creator Budget. There is no separate sale funding source. Verified-view baselines are captured once and full unpaid blocks are rewarded using the immutable Campaign snapshot. Lower provider counts are preserved as anomalies, not authoritative reductions.

Hybrid Offer QR is opaque, hash-only in persistence, five-minute, party/allocation-bound and one-use. Customer, Cashier and permitted Business owner/admin checkout converge on one financial engine. Payout eligibility uses effective Admin thresholds across Campaigns; a prepared payout pins one threshold amount, and only Admin-confirmed external payment deducts it, leaving carry-forward. Platform settlement uses the existing revenue accounting and allows partial amounts.

Phase 4 did not implement UI or live providers. The engine specification explicitly records bounded Phase 4 content-attribution, pause-accounting and currency-rounding interpretations for owner review before live financial integration; it does not change Business ownership, Admin pricing authority or non-refundable committed funding.

## Phase 5 role-specific Web/PWA

The [Phase 5 Web/API design](../architecture/PHASE-5-WEB-API.md) specifies the implemented Business, Creator and Admin workspaces, minimum Customer/checkout compatibility, role projections and test-only identity/funding boundaries. UX-STANDARDS.md remains normative. No Business-type wallet threshold, separate sale-funding source, active Creator Budget reduction or ordinary Campaign refund was introduced.

Business funding confirmation and atomic Approve & Set Budget are explicit interactions. Publishing starts an already-due Campaign; future Campaigns require a later explicit Start Campaign action. Public eligibility data comes from a trusted adapter, not the Creator's request body. Admin Notifications currently exposes persisted activity only, not a recipient delivery/read-state engine. Live authentication, verified social and trusted deposit confirmation remain unconnected and require separately authorized integration work.

## Phase 6 security and operational boundaries

The [Phase 6 readiness specification](../deployment/PHASE-6-READINESS.md) and [security audit](../security/PHASE-6-SECURITY-AUDIT.md) extend, not replace, the approved financial and UX architecture. A durable role/user-targeted inbox now sits on the same outbox, with read state and retry/failure tracking. Admin activity history remains separate from the recipient inbox. Financial configuration notices apply at the effective boundary and do not change historical Campaign pricing.

Production-compatible authentication verifies Firebase identity but resolves roles only from trusted V3 data; Development identities/funding cannot be enabled in Pilot/Production. Manual deposit submission, when explicitly configured, remains pending until an active Admin confirms external receipt and the existing wallet/journal transaction commits. Any positive amount is supported. No new financial engine, Business-type threshold or automatic committed-funds refund exists. Financial writes default paused outside Development.

Live Firebase client wiring/provisioning, social/payment/push credentials, legal content publication, physical devices and actual environment/backup/rollback acceptance remain explicitly gated. The PWA never queues offline financial writes or caches private account/QR data. The Worker delivers operational events and marks QR expiry; it does not silently post payouts, settlements, refunds or lifecycle money movements.
