# Operational delivery, retention and resource policy

## Durable delivery

Financial/lifecycle outbox inserts remain in the originating transaction. Worker selects bounded rows using `FOR UPDATE SKIP LOCKED`; in-app notifications and the delivery cursor commit together. Unique `(UserId, Role, SourceKey)` prevents duplicate in-app messages. A stable key and canonical event producer prevent audit/domain duplicates. Fanout seeks a bounded recipient cursor; new memberships after an event has been processed are not retroactively notified. Inactive users are excluded. Campaign and payout routes are server-built; messages contain no private financial splits or contact details.

Targets: Creator application → owning Business; approve/reject → Creator; budget assigned/increased → Creator; funded/published → Business/Admin; view reward → Creator; exhausted budget → Business/Creator; payout eligibility/paid → beneficiary; effective configuration → only roles whose visible pricing/thresholds changed; deposit submission → Admin and review → owning Business. QR scan noise does not create notifications. Existing Campaign snapshots are never repriced by a notification.

Delivery retries use exponential delay capped at 300 seconds, five failures maximum, with safe error codes and permanent failure state. In-app functionality does not require push. Optional `INotificationPushProvider` carries safe title/route + notification ID; live push remains disabled. Future push must use that ID for provider/client deduplication because network delivery is at-least-once across crashes. No broker is introduced. Inspect failed rows via approved DB/operational tooling; repair only the underlying cause and separately authorize a redrive. Do not rewrite immutable event payloads.

Scheduler uses a transaction-scoped PostgreSQL advisory lock. A durable effective-version checkpoint emits notifications only when a version actually becomes current, never merely when scheduled. If several versions passed while Worker was down, the latest current version is announced; obsolete intermediate versions do not generate stale notices. Worker expires issued QR sessions in bounded batches without deleting them or posting financial entries. No automatic Campaign refund/completion or payout execution is added.

## Retention — conservative defaults

No automatic deletion is enabled in Phase 6. Retention windows are deployment policy, not permission to delete. Legal/owner approval and a tested archival mechanism must precede a purge job.

| Data | Phase 6 policy |
|---|---|
| Financial journals/lines, wallet/earning/cashback entries, payouts, settlements, reservations, deposit reviews | Preserve; never purge via operational cleanup |
| Legal acceptance and accounting/lifecycle audit | Preserve until legally reviewed retention policy; no cleanup job |
| Financial/lifecycle idempotency | Preserve with the referenced financial operation; expiry must never permit a second posting |
| QR sessions | Five-minute usability; retain hashed historical binding/status, mark expiry only; used-sale references preserved |
| Provider evidence | Persist immutable evidence references, no raw credentials; retain attributable evidence with financial audit |
| Processed outbox and in-app notification history | Retain now; candidate 90-day operational archival only after owner/privacy review, no silent deletion |
| Delivery error details | Owned error code only; attempts/backoff/failure state retained |
| Logs | Operational recommendation: rotate 10 MiB × 3 per service; restricted access; redact at API and edge |
| Screenshots/test outputs | Local artificial data only, never ship in images; retain acceptance evidence per release; prune only specifically approved old evidence after archival |

## 2 vCPU / 4 GB / 80 GB deployment budget

No images build on the server. Proposed initial memory ceilings: PostgreSQL 1 GiB, API 512 MiB, Worker 256 MiB, optional static Web/edge 128 MiB, leaving OS/current-service headroom subject to measured coexistence capacity. This is a sizing proposal, not a verified co-deployment guarantee. Do not add these services beside existing workloads without checking actual headroom and disk/backup growth.

API connection pool capped at 20; Worker 5, minimum 0, connect timeout <=10s, command timeout 30s. Worker batch default20/max50; recipients default100/max200; interval5s (2–60); cycle cancellation30s; push timeout5s. API concurrent requests16, request JSON32KiB, headers16KiB/40, header timeout15s; uploads unsupported. PostgreSQL max_connections must cover bounded service pools, migration/backup/monitoring headroom, not hundreds of idle connections. Use private network/no public DB port and runtime roles without schema-owner/DDL privileges. Financial triggers must remain enabled.

Readiness: DB connection + no pending migrations + effective config + active mapped Admin (non-development) + fresh successful Worker heartbeat when enabled. `/health/live` reports process liveness only. Worker `--check-health` supplies container exec health. Admin `/api/admin/operations` exposes heartbeat/backlog/failure counts and counters for financial, QR, concurrency, provider, outbox/push and rate-limit failures. Metrics are process counters (reset on restart), not a billing ledger. Alert on readiness failure, permanent delivery failures, oldest backlog growth, repeated financial/concurrency errors, low disk and backup age. Realistic load/soak and backup growth measurements remain a controlled-environment prerequisite.
