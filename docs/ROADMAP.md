# V3 implementation roadmap

0. Architecture foundation (this deliverable): boundaries, invariants, ERD, state machines, permissions, environment and CI design.
1. Domain core: identities, legal acceptance, wallet, append-only journal, configuration versions, Promotion and allocation state machines.
2. Application/API commands: authorization, idempotency, concurrency, deposits, reservations, publishing, applications, approvals, allocations.
3. Persistence: EF/PostgreSQL mappings, a new V3 initial migration, transactions, journal reconciliation, idempotency, concurrency, audit and durable outbox. Background delivery/notifications remain later work.
4. Verified measurement and finance: implemented provider abstraction (test provider only), view rewards, Hybrid QR/checkout transactions, payout thresholds/records and settlement foundation. Live provider/payment integrations and HTTP adapters remain future work. See `docs/finance/PHASE-4-FINANCIAL-ENGINE.md`.
5. Role Web/PWA: implemented Admin control tower, Business Campaign workspace, Creator discovery/content/earnings, Customer Offer and shared Cashier/Business checkout with isolated development adapters. Follow `docs/product/UX-STANDARDS.md`; see `docs/architecture/PHASE-5-WEB-API.md` for scope and deferred connections.
6. Security and acceptance: implemented server-side auth/role/privacy hardening, bounded validation/rate limits, durable notifications/Worker, safe live-adapter boundaries, PWA/camera operational states and isolated regression acceptance. See `docs/deployment/PHASE-6-READINESS.md`; physical devices and live load/restore/rollback drills remain explicit prerequisites, not claimed completed.
7. Environment promotion: separate Development/Pilot/Production manifests, external image builds, controlled Pilot rollout, then separately approved Production.
