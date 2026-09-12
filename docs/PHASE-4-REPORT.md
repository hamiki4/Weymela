# Phase 4 completion report

Project: `/opt/WeymelaV3`. Verification date: 2026-09-11 UTC.

## Implemented scope

| Requirement | Result |
| --- | --- |
| Verified-view engine | Full unpaid blocks, immutable baseline, carry-forward, anomaly history, isolated Creator Budget spend, no partial block payment |
| Provider abstraction | IVerifiedViewProvider; test implementation only, no live social integration |
| Offer QR | Five-minute opaque random token, hash-only persistence, bound parties/allocation, wrong-Business protection, one-use and exact retry |
| Checkout | One SERIALIZABLE transaction posts sale, all earnings, budget/wallet consumption, QR use, journal, audit, idempotency and outbox |
| Funding source | View and full sale costs use only the attributed CreatorAllocation; never Available or another allocation |
| Payout eligibility | Effective Admin threshold across accumulated Creator view/commission earnings or Customer cashback |
| Payout records | One pending threshold-sized candidate, pinned version, Admin external-confirmation MarkPaid, carry-forward and duplicate prevention |
| Platform settlement | Existing revenue/settlement journal accounting, positive partial settlement, Admin-only summary/history |
| Admin financial contracts | Campaign and per-Creator budget/view/sale/earnings/cashback/Platform metrics |
| Role privacy | Business total costs without internal split; Creator own budget/earnings; Customer public offer and own cashback history |
| Migration | 20260911233032_AddViewRewardsQrAndPayouts; additive, forward-only; InitialV3Schema unchanged |
| Tests | Domain 76, Application 24, PostgreSQL 116: **216 passed, 0 failed, 0 skipped** |
| Release build | PASS, 0 warnings, 0 errors |
| EF pending-model check | PASS, no pending changes |
| Whitespace | git diff --check PASS; supplemental all-source whitespace check PASS |
| External boundary | No UI, HTTP checkout, live integrations, deployment or Docker image build |
| Phase 5 readiness | Ready for separately authorized role-specific Web/PWA work, not live-money/deployment acceptance |

## Verification commands

Run in the isolated V3 source workspace with the existing .NET 10 SDK image. PostgreSQL tests use disposable PostgreSQL 17 Testcontainers, one temporary database per test; no Pilot/Production connection is accepted.

- `dotnet test Weymela.slnx -c Release --no-build`: final 216 passed, 0 failed.
- `dotnet build Weymela.slnx -c Release`: zero warnings/errors.
- `dotnet-ef migrations has-pending-model-changes --project src/Weymela.Infrastructure --configuration Release --no-build`: no changes.
- `git diff --check` and `git diff --no-index --check /dev/null <source-file>` for all source files (the new repository's source remains untracked).
- Generated forward migration SQL reviewed: 5 new tables, 17 explicit indexes, 10 restrictive foreign keys, 5 CHECK constraints, 12 additional triggers, 6 helper functions. Explicit Version and PostgreSQL xmin protect new mutable entities. Money numeric(18,2); existing rates numeric(9,4). No forward DROP/TRUNCATE/DELETE or prior migration modifications.

The five new tables are CommercePermissions, CreatorPromotionParticipations, OfferQrSessions, PayoutRecords and ViewRewardReceipts. Existing verification history gains raw reported views, anomaly/baseline flags and participation reference; PlatformSettlements gains SettledBy. No other financial engine/table set replaces the existing accounting.

## Defects found and fixed

1. Npgsql wraps PostgreSQL serialization failures in InvalidOperationException around DbUpdateException. Unit of Work now identifies nested SQLSTATE 40001/40P01 and returns ConcurrencyConflict without unsafe automatic retries. The simultaneous-scanner test still requires exactly one sale and one financial posting.
2. An exhausted allocation previously rejected a legitimate top-up despite remaining Campaign reserve. It now accepts only new positive funding from that reserve; original consumed amounts/earnings remain unchanged. The existing consumed-funds test was corrected to assert this locked rule, not removed.
3. Exactly exhausting a Creator Budget now produces FundingRequired state and an outbox event for both view and sale processing.
4. Existing completion operations now also close the new content participation; completion continues to preserve committed Campaign reserve.
5. Payout eligibility/preparation validate persisted subject membership, including pending-candidate replay paths. New payout accounting guards reconcile account credits less paid amounts and the payout journal.
6. The expanded suite exposed one idle connection pool per completed temporary database, exhausting the test server's connection limit. Pooling is disabled only in the disposable per-test connection string. Runtime pooling is unchanged. All 116 PostgreSQL tests passed afterward.

## Test additions

93 additional independently named cases/rows: 23 Domain, 10 Application, 60 PostgreSQL. Coverage includes baseline once, pause/resume, stale/lower counts, full blocks/remainder, insufficient/no partial rewards, snapshot prices, both view modes, QR hashes/expiry/replacement/wrong Business/replays/parallel Cashiers, sale splits and isolated budgets, injected rollback, payout thresholds and carry-forward, payment replay, effective threshold pinning, partial settlement, actual role query results, private-field contracts, legal go-live gates, SQL history protection and additive schema consistency.

## Owner review before live integration

The current implementation explicitly documents these interpretations in [the financial engine specification](finance/PHASE-4-FINANCIAL-ENGINE.md):

- Independent two-decimal, midpoint-away-from-zero beneficiary rounding; total charge is the sum of rounded shares. At very small purchases this can differ from rounding the aggregate percentage once.
- One content item per CreatorAllocation and exclusive attribution per provider/content pair; multi-content attribution is not implemented.
- Pauses stop processing but do not reset baseline; cumulative verified views can qualify after resume.
- Live verification/identity adapters and external-payment confirmation operating controls require later selection/review.

These are not permission to begin Phase 5 or deploy. No external transfer is initiated by recording a payout or settlement.

## Files created/changed

All changes are confined to V3. Initial migration and its integrity helper retain their original SHA-256 values. API, Worker and Web program code and deployment workflows are unchanged. No commit, staging or push was performed.

- `README.md`
- `docs/ROADMAP.md`
- `docs/architecture/API-BOUNDARIES.md`
- `docs/architecture/ERD.md`
- `docs/architecture/PERSISTENCE.md`
- `docs/architecture/PROMOTION-LIFECYCLE.md`
- `docs/deployment/AddViewRewardsQrAndPayouts.sql`
- `docs/finance/FINANCIAL-MODEL.md`
- `docs/finance/PHASE-4-FINANCIAL-ENGINE.md`
- `docs/product/WEYMELA-V3-PRODUCT-SPEC.md`
- `docs/security/ROLE-PERMISSIONS.md`
- `src/Weymela.Application/Finance/FinancialContracts.cs`
- `src/Weymela.Application/Finance/FinancialProjections.cs`
- `src/Weymela.Domain/DomainModel.cs`
- `src/Weymela.Domain/Commerce/OfferQrSession.cs`
- `src/Weymela.Domain/Creators/CreatorPromotionParticipation.cs`
- `src/Weymela.Domain/Finance/PayoutRecord.cs`
- `src/Weymela.Domain/Finance/SnapshotPricing.cs`
- `src/Weymela.Infrastructure/Finance/AttributableFinance.cs`
- `src/Weymela.Infrastructure/Finance/CheckoutService.cs`
- `src/Weymela.Infrastructure/Finance/CommerceAccessPolicy.cs`
- `src/Weymela.Infrastructure/Finance/FinancialOperation.cs`
- `src/Weymela.Infrastructure/Finance/FinancialQueries.cs`
- `src/Weymela.Infrastructure/Finance/PayoutService.cs`
- `src/Weymela.Infrastructure/Finance/VerifiedViewService.cs`
- `src/Weymela.Infrastructure/Persistence/ServiceRegistration.cs`
- `src/Weymela.Infrastructure/Persistence/WeymelaDbContext.cs`
- `src/Weymela.Infrastructure/Persistence/Configurations/Phase4Configuration.cs`
- `src/Weymela.Infrastructure/Persistence/Migrations/20260911233032_AddViewRewardsQrAndPayouts.Designer.cs`
- `src/Weymela.Infrastructure/Persistence/Migrations/20260911233032_AddViewRewardsQrAndPayouts.cs`
- `src/Weymela.Infrastructure/Persistence/Migrations/Phase4IntegritySql.cs`
- `src/Weymela.Infrastructure/Persistence/Migrations/WeymelaDbContextModelSnapshot.cs`
- `src/Weymela.Infrastructure/Persistence/Records/CommercePermission.cs`
- `src/Weymela.Infrastructure/Persistence/Records/FinancialConfigurationVersion.cs`
- `src/Weymela.Infrastructure/Persistence/Transactions/CreatorBudgetCommands.cs`
- `src/Weymela.Infrastructure/Persistence/Transactions/EfUnitOfWork.cs`
- `src/Weymela.Infrastructure/Persistence/Transactions/LifecycleCommands.cs`
- `tests/Weymela.Application.Tests/Phase4ContractTests.cs`
- `tests/Weymela.Domain.Tests/ParticipationInvariantTests.cs`
- `tests/Weymela.Domain.Tests/Phase4FinanceTests.cs`
- `tests/Weymela.Infrastructure.Tests/Phase4CheckoutTests.cs`
- `tests/Weymela.Infrastructure.Tests/Phase4PayoutTests.cs`
- `tests/Weymela.Infrastructure.Tests/Phase4ProjectionAndIntegrityTests.cs`
- `tests/Weymela.Infrastructure.Tests/Phase4Scenario.cs`
- `tests/Weymela.Infrastructure.Tests/Phase4ViewTests.cs`
- `tests/Weymela.Infrastructure.Tests/PostgresFixture.cs`
- `tests/Weymela.Infrastructure.Tests/Scenario.cs`
- `docs/PHASE-4-REPORT.md` (this report)

No V2, existing Pilot/Production, Firebase, DNS, TLS, IP, Android signing material or backups were changed. Phase 5 has not begun.
