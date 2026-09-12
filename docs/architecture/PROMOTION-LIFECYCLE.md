# Promotion lifecycle and state machine

States: `DRAFT → FUNDED → PUBLISHED → ACTIVE → COMPLETED`. `BUDGET_EXHAUSTED` is a terminal financial stop that may transition to `COMPLETED`; `CANCELLED` is allowed only through an explicitly authorized business/legal rule. Publishing requires an atomic successful reservation.

Business creates type, total budget, requirements, targeting, dates, and content brief. A reservation moves Wallet Available to Reserved. Applicants can only request to join. Business approval atomically assigns an allocation no greater than unallocated Promotion budget. Each allocation has Original, Used, Remaining and an immutable audit trail.

Verified views and verified sales charge only the relevant Creator allocation, fail closed when Remaining is insufficient, and never create negative balances. Ordinary completion keeps unused Campaign funds committed; unused Creator amounts return to Campaign unallocated reserve with a balancing journal. Returning committed funds to Business Available requires a future authorized exception. Persisted lifecycle commands record actor, timestamp, idempotency where financial, and correlation/audit references.

## V3 allocation policy

Before participation is active, Business may set the Creator allocation. After activation, a Business may only increase it using the Promotion's remaining unallocated reserved budget; reductions and reclaiming earned/consumed funds are forbidden. On participation completion, unused Creator allocation returns to the Promotion's unallocated reserved budget, not to Business Available Balance. Exceptional reversals require Platform Admin authorization and compensating journal entries.

Phase 4 content participation is separate from the budget: Active / Paused / FundingRequired / Completed. Its baseline and rewarded count survive pause/resume. Budget exhaustion never partially rewards a view block. A top-up may reopen an exhausted allocation when the Campaign still has unallocated reserve; it cannot reopen a completed Campaign or reclaim earnings. Completing a Creator Budget/Campaign also closes the persisted content participation. See [financial engine details](../finance/PHASE-4-FINANCIAL-ENGINE.md).
