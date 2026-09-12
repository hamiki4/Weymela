# Phase 3 persistence

The implementation is in `src/Weymela.Infrastructure/Persistence`. This is the first V3 schema, named `v3`, with migration `20260911225904_InitialV3Schema`. No V2 migration or database is used.

## Command boundary

`FinancialCommands` is the transactional persistence decorator for the existing Application handlers. Hosts register it with `AddWeymelaPersistence` and supply an explicit environment-specific connection string. API/Worker hosts are intentionally not wired to a database in Phase 3.

Each command begins a PostgreSQL transaction before loading state. Repositories track/stage aggregate changes; they never independently commit. `EfUnitOfWork` saves and commits journals, operational entries, audit, outbox and idempotency together. Failure rolls back and clears tracked objects, including failures after an intermediate flush. Nested command transactions and automatic financial retries are rejected.

Phase 3 integrated commands: deposit credit, campaign creation/funding/publishing/activation/completion, Creator application/review, Creator Budget assignment/increase/completion and Admin financial configuration version creation. Phase 4 now adds verified-view, QR checkout and payout/settlement recording services; see [Phase 4 financial engine](../finance/PHASE-4-FINANCIAL-ENGINE.md). No external transfer or HTTP checkout handler is implemented.

The Phase 2 application services remain persistence-agnostic building blocks; future hosts must invoke the transactional decorator rather than call a handler and save individual repositories.

## Mapped entities

| Area | Tables |
| --- | --- |
| Business funds | BusinessWallets, WalletEntries, PromotionReservations |
| Campaigns | Promotions, PricingSnapshots, PromotionBudgetEntries |
| Creators | CreatorApplications, CreatorAllocations, PromotionViewVerifications |
| Commerce | VerifiedSales |
| Earned funds | CreatorEarningsAccounts, CreatorEarningEntries, CustomerCashbackAccounts, CustomerCashbackEntries |
| Platform | PlatformRevenueEntries, PlatformSettlements |
| Admin pricing | FinancialConfigurations, FinancialConfigurationVersions |
| Accounting | FinancialJournals, FinancialJournalLines |
| Legal | LegalDocumentVersions, LegalAcceptances |
| Reliability | IdempotencyRecords, OutboxMessages, AuditEvents |

Eligibility is owned within Promotions. Each immutable Promotion pricing snapshot has a separate owned row; the two pricing structures within a configuration version are owned columns in that version. Money remains a domain value object mapped to explicit numeric columns. Phase 4 adds the separate content/baseline CreatorPromotionParticipation, not another allocation or notification engine. The durable outbox is the future notification/worker handoff.

Business/Creator/Customer identity IDs are external domain references until V3 identity persistence is implemented. They are not foreign keys into V2 or Firebase. Financial and operational entity relationships inside V3 use restrictive foreign keys.

## Precision and currency

Money uses `numeric(18,2)`; rates use `numeric(9,4)`. Persistence rejects excess fractional precision or amounts outside numeric range before PostgreSQL could silently round. The current persistence currency is ETB; a non-ETB Money is rejected, never silently converted. Multi-currency accounting would require an explicit later owner decision and migration.

Wallet TotalBalance is a stored generated column, AvailableBalance + ReservedBalance. Campaign RemainingBudget and allocation RemainingAmount are stored generated differences. AllocatedBudget is a cached projection updated from the loaded child allocations and checked by a deferred PostgreSQL constraint trigger.

On Creator completion, the completed allocation continues to record its original/used/unused amounts for history. Its consumed amount remains included in campaign committed accounting; only its unused portion returns to campaign unallocated reserve. Completed allocations cannot consume again.

Normal campaign completion likewise preserves committed wallet reserve. It reclassifies unused Creator amounts back to campaign unallocated reserve, without a Business Available credit. Earlier Phase 0 wording implying an automatic refund is superseded by the locked Phase 2 non-refundable commitment policy.

## Journal authority

FinancialJournal + FinancialJournalLine is the authoritative accounting event. WalletEntry and PromotionBudgetEntry are immutable operational references to that journal, useful for wallet/campaign history; neither has independent balance mutation behavior.

| Operation | Debit | Credit |
| --- | --- | --- |
| Deposit | CashClearing | BusinessAvailable liability |
| Fund campaign | BusinessAvailable liability | CampaignUnallocatedReserve liability |
| Assign/top up | CampaignUnallocatedReserve liability | CreatorAllocatedReserve liability |
| Return unused Creator Budget | CreatorAllocatedReserve liability | CampaignUnallocatedReserve liability |

Journal metadata carries actor, source, reference, correlation, idempotency and optional Business/Promotion/Creator/Customer references with indexes. Creator earnings, cashback, Platform revenue and settlements have required journal foreign keys. Normal repositories expose no history update/delete operation.

Both EF guards and PostgreSQL triggers reject edits/deletes of financial, legal and audit history. A deferred database trigger enforces balanced posted journals. Lines can only be appended while their new parent journal is part of the current uncommitted transaction (including EF savepoint subtransactions); later inserts into posted journals are rejected. Wallet projections must reconcile to journal lines at commit. Platform revenue entries must match their classified journal revenue; settlements cannot exceed accrual. Platform accrued/settled/unsettled is derived from revenue and settlement entries, without another revenue ledger.

VIEW_ONLY sales are rejected in the domain and database. Sale rows have a composite allocation/Promotion/Creator foreign key. Cashback entries must reference the sale's actual Customer and cashback amount; a verified sale can generate cashback once. Phase 4 implements hash-only QR sessions and transaction orchestration using these same financial records.

## Concurrency and idempotency

Wallet, Promotion and CreatorAllocation use a numeric Version plus PostgreSQL `xmin`. Repositories check the caller's expected Version against the original loaded value; EF's update predicate checks both tokens. This prevents stale tracked sessions from reserving the same wallet or overallocating a campaign. Earnings/cashback account rows also use xmin.

PostgreSQL xmin is mapped using Npgsql's supported row-version approach: [Npgsql concurrency tokens](https://www.npgsql.org/efcore/modeling/concurrency.html). The UoW explicitly encloses EF saves in a transaction: [EF transactions and savepoints](https://learn.microsoft.com/en-us/ef/core/saving/transactions).

DbUpdateConcurrencyException is translated to ApplicationFailure(ConcurrencyConflict). No unsafe automatic retry occurs. Unique-key races return an explicit conflict; clients may replay the same request/key in a fresh transaction to obtain its committed result.

Idempotency uniqueness is ActorId + OperationType + Key. Fingerprints include the business/resource and exact monetary request, not request timestamps or stale precondition versions. Replays return the stored reference. A changed amount, resource or Business with the same scoped key is rejected. Keys remain available independently to other actors and operation types.

## Pricing and legal

The singleton PlatformPricing configuration has immutable versions. Effective lookup selects EffectiveFromUtc <= evaluation time, then newest effective date and highest version (Id is the final stable tie-breaker). Future versions do not apply early.

The approved Phase 2 creation behavior is retained: CreatePromotion captures authoritative Admin pricing as an immutable snapshot. Funding also rechecks the current effective type minimum budget. It does not rewrite the existing quote/snapshot. Later Admin changes never rewrite funded campaign history.

Legal acceptance uniqueness is UserId + Role + DocumentVersionId. The gate resolves the current effective document version and requires that exact acceptance. A missing required document or acceptance fails closed.

## Outbox and audit

OutboxMessage stores Id, EventType, JSON payload, OccurredAtUtc, nullable ProcessedAtUtc, AttemptCount and LastError. An index selects pending records. Domain events and audit envelopes are inserted inside the financial transaction. No broker or delivery worker runs in Phase 3.

AuditEvent is actor-oriented operational history, distinct from double-entry accounting. It links lifecycle/configuration actions to correlation and role-specific resource IDs. No tokens, credentials, contact details or connection strings are included.

## Migration review and testing

`docs/deployment/InitialV3Schema.sql` is the generated forward SQL for offline review (only trailing whitespace normalized). It creates 25 V3 tables plus EF migration history, 50 explicit indexes, 29 foreign keys and 19 CHECK constraints. Integrity protection adds 11 explicit triggers plus 17 append-only history triggers generated by the SQL loop. No DROP/TRUNCATE/DELETE or existing schema changes occur in its forward path. EF's generated Down path is a schema teardown for a new development database and is not a financial-data rollback procedure.

Additional PostgreSQL constraints/triggers live in the immutable initial migration helper `IntegritySql`, including journal balancing/immutability, wallet reconciliation, allocation projection reconciliation, snapshot version linkage, Platform settlement bounds and cashback eligibility. They are intentionally database-specific and are exercised through SQL-bypass tests as well as EF tests.

Integration tests use Testcontainers PostgreSQL 17. Each test creates a unique database in a disposable container; the only exposed test port binds to 127.0.0.1. No external connection string is accepted by the fixture. The design-time EF factory points to an intentionally unusable localhost port and is used only for offline migration generation.

Run on a development machine with .NET 10 and Docker:

```sh
dotnet test tests/Weymela.Domain.Tests -c Release
dotnet test tests/Weymela.Application.Tests -c Release
dotnet test tests/Weymela.Infrastructure.Tests -c Release
dotnet build Weymela.slnx -c Release
git diff --check
```

No Dockerfile, image build, deployment, external service integration or Web UI is part of Phase 3.
