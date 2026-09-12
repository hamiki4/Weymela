# V3 entity relationship specification

`PlatformFinancialConfiguration` 1—* `FinancialConfigurationVersion`; versions feed immutable `PricingSnapshot` references on `Promotion`, `CreatorAllocation`, and financial events.

`Business` 1—1 `BusinessWallet` 1—* `WalletEntry`; `Business` 1—* `Promotion` 1—1 `PromotionReservation` 1—* `PromotionBudgetEntry`; `Promotion` 1—* `CreatorApplication` 1—1 `CreatorAllocation`.

`Promotion` 1—* `PromotionViewVerification` and 1—* `VerifiedSale`; events reference exactly one allocation. `Creator` 1—1 `CreatorEarningsAccount` 1—* `CreatorEarningsEntry` 1—* `CreatorPayout`; `Customer` 1—1 `CustomerCashbackAccount` 1—* `CustomerCashbackEntry` 1—* `CustomerPayout`. `PlatformRevenueEntry` and `PlatformSettlement` are separate aggregates. `FinancialJournal` 1—* `FinancialJournalLine`; all financial aggregates reference journal IDs and audit/correlation records.

Authentication identities, legal acceptances, notifications, idempotency records, and audit events are cross-cutting entities with tenant/resource scoping.

## Phase 4 concrete persistence relationships

PricingSnapshot is owned by Promotion; allocation and event IDs resolve that immutable snapshot through Promotion rather than copying another mutable price. Approved applications may exist before allocation, so approval-to-allocation is optional until Business assigns a budget.

CreatorAllocation 1—0..1 CreatorPromotionParticipation; participation 1—* PromotionViewVerification and ViewRewardReceipt. Each receipt references one FinancialJournal. OfferQrSession binds Customer/Business/Promotion/Creator/CreatorAllocation and has at most one VerifiedSale; each QR source reference is unique across sales.

CreatorEarningsAccount or CustomerCashbackAccount 1—* PayoutRecord, with exactly one beneficiary per record and at most one Eligible candidate per beneficiary. Paid records reference a FinancialJournal and pinned FinancialConfigurationVersion. PlatformSettlement remains the existing journal-linked entity, now with SettledBy. CommercePermission contains only trusted subject/role/business authorization IDs and active/checkout flags, not contact details or Firebase credentials.
