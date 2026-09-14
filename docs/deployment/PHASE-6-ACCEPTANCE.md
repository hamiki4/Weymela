# Phase 6 acceptance record — 2026-09-12 UTC

Outcome: **READY FOR PHASE 7 CONTROLLED PILOT PREPARATION**, subject to the explicitly deferred live-enablement and manual prerequisites in [PHASE-6-READINESS.md](PHASE-6-READINESS.md). This is not deployment, financial-unfreeze, or physical-device acceptance.

## Automated results

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Domain | 80 | 0 | 0 |
| Application | 31 | 0 | 0 |
| Infrastructure / isolated PostgreSQL | 187 | 0 | 0 |
| HTTP API integration | 75 | 0 | 0 |
| Frontend component/behavior | 54 | 0 | 0 |
| Rendered browser/E2E | 26 | 0 | 0 |
| **Total** | **453** | **0** | **0** |

Security-focused tests are included in these counts, not double-counted as another suite. The complete .NET solution suite was run. Its only remaining failure was the old exact two-migration history expectation; this was updated to require all three migrations and concurrency tokens on the new aggregates. The complete187-test PostgreSQL suite then passed on rerun. Earlier failed-run output is retained for traceability; `infrastructure-final.trx` is the final result. No test was deleted or disabled.

.NET Release build: PASS, zero warnings/errors. Frontend production build: PASS. Offline EF pending-model check: PASS, no changes since the latest migration. Native `git diff --check`: PASS. The repository is still wholly untracked/no commits, so a supplementary source inventory also checks all changed text for trailing whitespace and compares every included file with the pre-Phase-6 `/opt/WeymelaV3` source: no omitted files. Nothing was staged, committed or pushed.

Dependency audits: zero known .NET transitive or npm vulnerabilities; [license inventory review](../security/DEPENDENCY-REVIEW.md). These are point-in-time results; final release images have not been built or scanned.

## Evidence

Artifacts are under `/opt/WeymelaV3/.artifacts/` after source handoff; they are ignored by Git and are not runtime/release-image inputs:

- `phase6-dotnet/*.trx` — Domain/Application/API full-suite records and PostgreSQL final rerun; retain original failed migration-history result too.
- `phase6-frontend.json` — component/behavior results.
- `phase6-e2e-results.json` — all26 browser tests, no retries/skips.
- `phase6-screenshots/` —358 actual rendered captures with artificial test identities/data; mobile375/390/393/430, tablet768, desktop1366×768/1440×900/1920×1080. Extra security-state captures use the same widths.
- `phase6-dotnet-audit.json`, `phase6-npm-audit.json`, `phase6-dependency-inventory.json`.
- `phase6-source-integrity.json` — before-handoff source/file hashes and exact changed-file list; no source archive or release seal created.

Visual inspection included375px Business wallet,390px Creator inbox,1366px inbox and1440px camera-denied state. Browser assertions cover all major approved role screens, Campaign creation/funding/publishing/join/approval, Hybrid-only QR, pricing privacy, mobile cards, desktop tables, stable buttons, keyboard focus, offline/unauthorized states, and persisted notification reads. Synthetic full camera frames exercise the actual ZXing decoder and real isolated checkout. No live API response is mocked for the main lifecycle/checkout flows.

## Security / operational coverage

| Area | Accepted source behavior |
|---|---|
| Authentication | Firebase-compatible signed token verifier with test keys; exact project/issuer/audience/recent authentication; trusted DB roles; encrypted environment-isolated cookie keyring; Web email-code/custom-token adapter implemented, live delivery/signing/provisioning deferred |
| Authorization | Explicit route inventory, other-role denial, other-Business/Creator/Customer IDOR, Cashier reassignment and checkout-permission removal; no payload role assignment |
| Privacy | Role-specific DTOs, no private marketplace contacts or unnecessary cross-role finances; server-created safe notification routes |
| Inputs / rate limits | Bounded JSON including unknown-length requests, unsupported body rejection, positive/precision constraints, IDs/strings/dates; endpoint-specific429 and Retry-After |
| QR | Hash-only persistence, five-minute expiry, wrong-Business isolation, replacement/replay/concurrency regression, no token logging; camera stream cleanup |
| Finance | Existing transactional/idempotent/versioned engine retained; view/sale/payout/settlement and Campaign/Creator reserve reconciliation; no extra ledger or refund path |
| Deposits | Explicit disabled/manual provider boundary; pending until active Admin confirms; exact credit journal in same serializable transaction; duplicate/rollback/concurrent approval protection |
| Social | Provider-neutral capability/ownership/content/evidence/health validation and timeout; no live credentials/provider calls |
| Notifications | Durable in-app user/role target + unread/read-one/read-all; canonical deduplication, bounded fanout, retries/permanent failure; optional push boundary remains disabled |
| Worker | Row locking, scheduler checkpoint, effective-notice targeting, expiry observation, safe failure codes/heartbeat; no silent financial posting |
| PWA / headers | Only anonymous offline assets cached; no private/financial/QR cache or offline write queue; controlled update reload; CSP/CORS/cookie/CSRF/camera/header tests |
| Legal / retention | Exact current version/hash consent; no hard-coded legal wording; conservative preserve-by-default retention and no financial deletion job |
| Observability / operations | Public minimal health/readiness, Admin backlog/failure/reconciliation views, structured correlation logs; backup/restore, resource and environment runbooks |

## Defects corrected during this pass

- Development-only authentication/funding and disconnected providers lacked a deployable fail-closed configuration boundary.
- Cashier session authorization did not recheck the current Business assignment/checkout permission at the policy boundary.
- Unknown-length JSON bodies needed an explicit middleware size cap in addition to Kestrel's limit.
- Operational outbox persistence had no recipient/read-state/retry/delivery pipeline; failures during notification commit now roll back and enter the same retry policy.
- New manual deposit approval needed a **deferred commit-time** exact-journal check, so EF insert ordering cannot cause a false rejection or allow an unjournaled credit.
- Camera cleanup now covers delayed permission resolution, screen exit and backgrounding; denial/unavailable names are recognized across browser error realms.
- Install/update/offline behavior needed safe install icons, release cache identity and explicit non-queued offline states.
- Test/build harness corrections: required import, exact new migration history, non-development fixture configuration, endpoint name, deterministic permission-error fixture and correct EF `--configuration Release` option. They do not change approved finance or weaken assertions.

## Remaining manual/owner gates

Physical iPhone Safari/PWA, Android Chrome/PWA and desktop webcam: **MANUAL PILOT CHECK REQUIRED**, not PASS. Actual TLS edge/header forwarding, live identity provisioning/client integration, live social evidence, manual-deposit receipt authority, legal content publication, capacity/load/soak, new-environment backup/restore/rollback rehearsal and signed external images remain prerequisites. Push and manual identity lookup stay disabled. No unresolved automated release-critical failure remains; these gates prevent any claim of live deployment readiness.

No V2/current Pilot/Production database, container, Firebase project, DNS/TLS/IP, Android signing material or existing backup was modified. Only disposable local acceptance hosts/databases were stopped/removed. No deployment, release image build, financial unfreeze, commit or push occurred. Phase7 was not started.
