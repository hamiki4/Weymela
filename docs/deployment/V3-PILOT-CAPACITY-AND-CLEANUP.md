# Read-only capacity and later cleanup inventory

Measured 2026-09-12 UTC on the current application server. Filesystem/container/network metadata only; no DB queries, container exec, service stops, pruning, file deletion or configuration changes. These are snapshots, not a load/soak result.

## Capacity finding: BLOCKED for side-by-side deployment today

| Resource | Observed |
|---|---|
| CPU | 2 vCPU |
| Root /opt /tmp filesystem | 75GiB total,68GiB used,4.2GiB available,95% utilization |
| RAM | 3814MiB total,1367MiB used,2446MiB available at first sample |
| Swap | 2047MiB total,1637MiB used,410MiB free at first sample |
| Docker images | 35 images,8 active;9.281GB;6.673GB reported reclaimable (NOT automatically safe to delete) |
| Docker containers | 9 running,zero stopped;719.3MB writable layers |
| Docker volumes | 8 total,6 active;1.246GB;700.1MB unreferenced |
| Docker build cache | 95 records,26.07GB raw/shared accounting;only851.9MB reported reclaimable |
| `/tmp` | approximately19GiB |
| `/var/log` | approximately1.2GiB,including824MiB journal |
| `/var/lib/docker/containers` | approximately3.7GiB including logs; do not truncate them during this task |
| `/opt/weymela/releases` / `backups` | 1.2GiB /230MiB; KEEP pending rollback/backup retention review |

Docker reports decimal GB/MB; `df`/`du` observations use binary units. Image/cache/shared-layer totals overlap: **do not add Docker's raw build cache26GB to image9GB as exclusive disk usage**, and never promise26GB recoverable. Swap is already substantially occupied; a brief `vmstat` sample was mostly idle with small page-in activity, not proof of peak-load headroom.

## Existing containers — KEEP / do not operate

| Container | Image / role | Existing listener | Sample memory |
|---|---|---|---|
| creatorpay-api-1 | creatorpay-api;current V2 Pilot |127.0.0.1:8080|18.63MiB |
| creatorpay-web-1 | creatorpay-web;current V2 Pilot |127.0.0.1:8081|1.02MiB |
| creatorpay-worker-1 | creatorpay-worker;current V2 Pilot |none|51.04MiB |
| creatorpay-postgres-1 | postgres:17-alpine;V2 Pilot |private5432|25MiB |
| creatorpay-prod-api-1 | weymela-prod-api:v1.0.1 |127.0.0.1:8082|15.86MiB |
| creatorpay-prod-web-1 | weymela-prod-web:v1.0.1 |127.0.0.1:8083|1.66MiB |
| creatorpay-prod-worker-1 | weymela-prod-worker:v1.0.1 |none|48.91MiB |
| creatorpay-prod-postgres-1 | postgres:17-alpine;Production |private5432|20.98MiB |
| creatorpay-migrator | dotnet/sdk:10.0;existing long-lived utility |none|~0.51MiB;700MB writable layer |

All eight application/database containers reported healthy. The migrator is running `sleep infinity`; it was not stopped or declared disposable. Both existing PostgreSQL services have only their respective V2 backend networks. Their databases/credentials were not inspected. Existing volumes containing data/proofs/keyrings remain protected.

## Proposed additional V3 footprint — estimates, not built measurements

- Memory caps: API512 +Worker256 +Web96 +dedicatedPG768 = **1632MiB (1.59GiB)**, plus Docker/OS overhead and temporary health-check processes. Estimated steady controlled-test demand ~500–1000MiB must be measured. Caps are not reservations; current V2 uncapped services can still compete.
- CPU quotas total1.60CPU; database0.50/API0.75/Worker0.25/Web0.10. This does not guarantee V2 headroom under contention; start with low tester concurrency and monitor load/swap/latency.
- Expected unpacked images: API~350–450MB,Worker~330–420MB,Web~60–100MB; shared .NET base reduces exclusive total. Existing PostgreSQL17 layers may be reused only if the approved digest matches. CI must report actual unpacked size, registry transfer size and base digests. No image was built/pulled for this estimate.
- Allow roughly1–2GiB initial additional image/layer disk and3–5GiB initial DB/WAL/backups/one rollback set growth, plus log ceiling~120MiB. Pull/extraction peaks need additional margin. **Require at least12GiB measured free before first V3 pull/migration**, then alerts at20% free or12GiB (whichever is higher) and a stop-growth/escalation policy.
- Current4.2GiB free is insufficient. Reclaiming roughly8GiB safely or expanding storage is needed before reconsideration. After cleanup authorization, remeasure RAM/swap and run separately approved controlled load/restore/rollback acceptance; do not infer safe coexistence from idle RSS alone.

## LATER cleanup candidates — nothing deleted now

| Exact candidate / group | Potential recovery | Conditions before any deletion |
|---|---|---|
| `/tmp/weymela-phase1-check`, `check2`, `check3`, `check4`, `check5`, `check6`, `/tmp/weymela-phase1-final` | ~3.63GiB combined | Compare/retain any unique accepted source/evidence; verify no open processes; explicit owner approval |
| `/tmp/weymela-v3-phase3-iPizsX`, `/tmp/weymela-v3-phase4-4GJJB8`, `/tmp/weymela-v3-phase5-w0GSOY`, `/tmp/weymela-v3-phase6-9D04Y3` | ~898MiB combined | Keep current Phase6 evidence until archived/verified; preserve authoritative `/opt/WeymelaV3`; approve exact candidates only |
| Unused tool images `node:22-bookworm`8a34c4ab3ea2, `node:22-bookworm-slim`83f487e0a634, `node:22-alpine`c610fcdfb1d5, `mcr.microsoft.com/playwright:v1.62.1-noble`dcc5531e9784 | ~5.71GB unique layers from Docker snapshot | Later external-CI transition; verify no active build/test consumer and re-pull availability; never remove current runtime/rollback images |
| Volume `weymela-e2e-dotnet-20260911` |628.3MB|Unreferenced; inspect ownership/content classification before authorization |
| Volume `43c9f163855e37c973452d71939b7c1218158846d6f38160db344bc1163cc3d7` |71.72MB|Unreferenced is not proof disposable; ownership unknown, quarantine/retain until identified |
| BuildKit source records `kw8vqgx1zm97`, `eq1k5basib08` (~426MB each) plus tiny source records | Docker reports851.9MB reclaimable total | Explicit scoped builder-cache review, no global Docker prune; shared records must not be counted twice |
| `/opt/weymela/releases` older Pilot archives | upper bound1.2GiB;safe recoverable amount unknown | Only after V3 acceptance and verified off-host source/rollback retention; current sealed/rollback artifacts KEEP |
| Historical `/var/log` and existing Docker logs | size~4.9GiB combined, safe recovery unknown | Separate log-retention/legal review; do not truncate active container logs or modify V2 logging now |

Conservative named non-overlapping potential after approval: ~3.63GiB temporary snapshots +5.71GB unused tool-image unique layers ≈ **8.95GiB**, subject to verification. Do not count currently protected rollback images, backups, anonymous volume, or logs as available headroom. Other temporary directories may contain sensitive/signing/backup material: do not bulk delete `/tmp`, `/opt`, Docker storage or shell globs.

KEEP unconditionally for this task: Production and current Pilot runtime/DB/proofs/keyrings, Firebase configuration, Android signing/upload material, all verified backups, rollback artifacts, existing source trees and current acceptance evidence. No dead test container was found. No cleanup command was executed or automated.
