# Phase 6 — controlled Pilot preparation prerequisites

This is a **source readiness package, not deployment authorization**. Phase 6 creates no live V3 database, modifies no existing Pilot/Production/V2 service, does not contact live Firebase, and builds no release Docker image. Phase 7 must be explicitly authorized separately.

Related evidence and boundaries: [security audit](../security/PHASE-6-SECURITY-AUDIT.md), [Worker/retention/resource policy](RETENTION-AND-WORKER.md), [backup/restore runbook](BACKUP-RESTORE.md), [locked UX standard](../product/UX-STANDARDS.md).

## Required configuration and secrets inventory

See repository `.env.example`: intentionally invalid/blank until deployment-owned values are supplied. Never copy an existing Pilot/Production `.env`, Firebase private key, Android key, or database credential into V3. Do not log expanded connection strings or tokens.

| Setting | Requirement |
|---|---|
| `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT` | Explicit `Pilot` or `Production`; separate environment identity |
| `ConnectionStrings__WeymelaV3` | NEW dedicated V3 PostgreSQL DB/account; required external credential; Pilot name prefix `weymela_v3_pilot`, Production `weymela_v3_prod`; never V2/shared DB |
| `V3__EnableDevelopmentIdentity` | `false` outside isolated Development; startup rejects otherwise |
| `V3__Auth__Provider`, `V3__Auth__FirebaseProjectId` | `Firebase` and exact owner-approved existing project identity; public identifier, not a role authority |
| `V3__Auth__CookieKeyDirectory` | Persistent protected mount, API write access only; isolate Pilot/Production and include in encrypted recovery procedure |
| `V3__Auth__CookieCertificatePath`, optional password | External current PFX with private key, mounted read-only for API; secret/private material never in source; plan overlapping key rotation and old-key recovery |
| `V3__AllowedOrigins__N` | Explicit HTTPS origins, no wildcard; Web origin must be included |
| `V3__PublicWebUrl`, `V3__PublicApiUrl` | Owner-approved HTTPS origins; do not assume V2 domains are available or alter DNS |
| `V3__Security__CameraPolicy` | `camera=(self), microphone=(), geolocation=(), payment=(), usb=()` |
| `V3__Security__TlsEdgeConfirmed` | `true` only after separately verifying HTTPS/header forwarding at approved V3 edge |
| `V3__Security__TrustedProxies__N` | Exact private proxy IPs if behind an edge; never trust arbitrary forwarding headers |
| `V3__FinancialWritesEnabled` | Defaults `false` outside Development; keep false during preparation; no HTTP unfreeze endpoint |
| `V3__Deposits__Mode` | `Disabled` or `ManualApproval`; manual approval requires documented proof-of-receipt authority; never fake Development credits |
| `V3__Social__Mode` | `Disabled`; live adapter/credential integration is deferred and unknown modes fail startup |
| `V3__Push__Enabled` | `false`; in-app inbox works without FCM; live push not connected |
| `V3__Worker__Enabled` | `true` for durable delivery; readiness requires fresh successful heartbeat |
| Worker batch/recipient/interval | Defaults 20 / 100 / 5 seconds; bounded maxima in startup validator |
| `V3__WebRoot` | Optional production build directory if API serves the Web; no source/test/static secret directory |

Secret inventory: separate database runtime/migration/backup credentials, API cookie protection private certificate/password, protected cookie keyring, future provider credentials only when an adapter is approved, and future registry/CI credentials outside source. Firebase project ID and Web public client configuration are not secrets, but remain owner-approved configuration. No Firebase Admin service-account private key is required for the current public-signature verifier. Worker requires no cookie certificate or Firebase private material. Existing Android signing/upload identity is outside V3 Phase 6 entirely.

## Runtime topology and resource guard

Proposed services: V3 API, V3 Worker, dedicated V3 PostgreSQL17, Web static assets (optionally served by API for initial same-origin validation), existing approved TLS edge only after separate authorization. Database port5432 private-network only; API internal8080 if selected by runtime configuration; Worker exposes no port; public443 only through the approved edge. These are design values, not allocated/created listeners on existing Pilot/Production.

Use versioned external-builder/registry images and immutable digests. No server-side image builds. Separate V3 network, DB credentials, cookie keys, volumes, log names and backup paths for Pilot/Production. Runtime roles should not own schema or disable triggers. A separately authorized migration role applies reviewed SQL; API/Worker do not auto-migrate or seed on startup. Pool caps API20/Worker5, request32KiB, concurrent requests16, bounded Worker cycles. See resource policy for proposed memory ceilings and disk/log limits. Actual coexistence headroom/load/soak must be measured before live placement; local correctness tests do not prove server capacity.

## Migrations

New additive migration: `20260912011149_AddOperationalSecurityAndNotifications`.

Five operational tables: IdentityBindings, PublicWorkspaceProfiles, DepositRequests, InAppNotifications, WorkerCheckpoints. Outbox adds retry/failure/cursor fields. Indexes enforce external identity scope, public IDs, deposit references/confirmation uniqueness, user-role-event deduplication and bounded delivery/expiry queries. Monetary deposit precision remains numeric(18,2); existing money/rate precision and accounting schema remain unchanged. Operational aggregates use explicit Version/xmin. Deposit references wallet/journal with restricted deletion.

Additive trigger hardening preserves reviewed deposits, exact approved-credit journal linkage (deferred to commit), notification identity/read history and immutable outbox envelopes. Existing QR guard is extended only for historical expiry observation; its binding/sale protection remains. Earlier migration files are unchanged. Review the forward-only SQL artifact before any separately authorized application. Never run a down migration or drop operational/financial history as an image rollback shortcut.

## Health, monitoring and backup gates

`/health/live`: process status only. `/health/ready`: bounded DB connectivity, no pending migrations, current pricing, mapped active Admin, and fresh Worker success if enabled. It returns only ready/not_ready publicly. `/api/admin/operations`: authorized heartbeat/backlog/oldest pending/failure counters and configured modes. `/api/admin/reconciliation`: authorized read-only accounting mismatch report. Worker supports `--check-health`. Financial/QR/concurrency/provider/delivery/rate-limit counters and structured correlation IDs support lightweight observation; no monitoring broker is required.

Before migration: verify a PostgreSQL custom-format backup, checksum, restore-list, isolated restore rehearsal and accounting totals. Preserve image digests, schema compatibility record, configuration hashes (not secrets), cookie key recovery material and previous backup. [Backup/restore runbook](BACKUP-RESTORE.md) requires exact environment guard and separate destructive-restore authorization. No real backup/restore was performed against existing Pilot/Production.

## PWA / camera acceptance

The approved layout is unchanged except operational inbox/deposit/offline/error states. Production manifest includes192/512 PNG and Apple180 icon derived from the approved vector. Service Worker caches **only anonymous offline HTML/CSS**. Authenticated pages, API results, tokens, QR and financial writes are never cached/queued. Hashed build assets and an injected release cache identity prevent stale offline assets; waiting updates require explicit user reload, not interruption of a payment. Existing key responsive flows plus errors/inbox/offline states are rendered at375/390/393/430/768/1366/1440/1920.

Automated synthetic-camera frames exercise the real decoder and real checkout; camera permission/unavailable/cleanup tests do not replace physical-device acceptance.

**MANUAL PILOT CHECK REQUIRED**, after separately authorized HTTPS V3 deployment:

- iPhone Safari and installed PWA: fresh permission grant, deny, deny→settings→allow, rear camera, portrait/landscape, background/foreground, navigate away, reopen, and no persistent camera indicator/stream.
- Android Chrome and installed PWA: same checks, camera absent/in use, slow network, interrupted permission prompt; confirm no stream after leaving.
- Desktop webcam: permission unavailable/denied, correct active device and cleanup; scan at every full-frame edge, not only the guide. The visual guide must not crop decoder input.
- All devices: real five-minute expiry/countdown/replacement, wrong Business then correct Business, no offline QR/payment submission, reconnect without automatic replay, manual update only after finishing checkout, install icon/start URL, stale-cache update, keyboard/focus/contrast and no horizontal overflow.
- Verify actual edge CSP/Permissions-Policy/CORS/HTTPS/HSTS and no query/token/body logging. Do not label these physical/edge checks PASS based on local Chromium.

## Deferred integrations / owner decisions

1. Authorize exact new V3 Pilot hosts, database, network and service budgets without modifying existing services.
2. Approve Firebase client sign-in adapter wiring, trusted identity/account provisioning and local-vs-Firebase-global revocation policy. The server verifier is tested, but no live sign-in or live Firebase project configuration was performed.
3. Publish legally reviewed current documents and deliver their exact text/hash through an approved acceptance presentation; no new live user is auto-accepted. No two-year restriction is hard-coded.
4. Decide whether manual deposit approval is enabled and who verifies external receipts. Approval API uses the existing ledger; bank/payment provider automation remains disabled.
5. Approve and implement live social adapter credentials/capabilities/evidence provenance before any real verified reward. Test metrics are Development-only. Push is optional and disabled; core in-app delivery is durable.
6. Keep manual identity lookup disabled until privacy-safe resolution/rate limits/authorization are approved; QR and future manual path must share one engine.
7. Approve RPO/RTO, retention/legal hold, alert ownership, backup encryption and restore/load/rollback rehearsals; these were not executed against live systems.
8. External CI/image signing/SBOM/registry/deployment manifests and versioned image verification belong to separately authorized preparation, not a Phase 6 deploy action. Existing CI remains a foundation, not a claim that release builds already exist.

Advancing to Phase 7 means preparing these controlled prerequisites. It does not mean live adapters, physical devices, backups, release images or deployment have already passed.
