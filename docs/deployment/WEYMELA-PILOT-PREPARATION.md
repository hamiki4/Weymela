# Phase 7 — side-by-side V3 Pilot preparation

Status: **prepared source, NOT deployment authorization**. Phase 0–6 acceptance remains 453 passed, zero failed. Phase 7 changes repository/CI/runtime preparation and documentation only: no application, financial, UI or migration changes. No live database, network, container, DNS, TLS, Firebase, signing key or V2 resource is created/changed. Nothing is committed or pushed.

## Decision and readiness gates

| Gate | Current result | Required next action |
|---|---|---|
| Safe source inventory / secret scan / workflow syntax | See [repository inventory](V3-REPOSITORY-INVENTORY.md) and local Phase 7 evidence | Owner chooses private GitHub owner/repository and explicitly authorizes initial commit/push |
| CI / registry | Workflows prepared, not executed remotely; digest-pinned actions, security gates, linux/amd64 images and migration artifact | Configure protections/permissions, run trusted CI, inspect signed digests and actual sizes |
| Capacity | **BLOCKED: root 95%, only 4.2 GiB available** | Specifically authorize cleanup or increase capacity; retain at least 12 GiB free before first pull/migration, including measured backup/rollback reserve; remeasure memory/load |
| Isolation | Dedicated V3 PostgreSQL/network/credentials chosen; no V2 network reuse | Approve subnet, paths, volume, port, resource ceilings and dedicated database roles |
| Firebase access | Server verifier, email-code/custom-token Web adapter and trusted bootstrap tool exist; no live provisioning performed | Inject public config, configure protected email delivery/signing, review revocation policy and provide the owner UID/UserId for controlled role provisioning; no Development identities outside Development |
| Deposit | Pending/manual approval API exists; no bank integration | Name receipt approver, approve test-money procedure and operator tooling; keep financial freeze until separately authorized |
| Social | Live credentials optional; no fake provider permitted in Pilot | Implement/review the audited Admin evidence adapter described in [adapter plan](V3-PILOT-ADAPTERS-AND-USERS.md); currently `Social=Disabled` |
| Legal | No final wording supplied or published; existing UI/gates do not yet cover the complete five-document live publication set | Owner-approved documents, exact hashes/version records and complete presentation/acceptance gates before live participants |
| Physical/operational acceptance | **MANUAL PILOT CHECK REQUIRED**, not PASS | Authorized isolated HTTPS run, devices, restore rehearsal, soak, reconciliation, notifications and rollback checks |

Missing live social API credentials alone do **not** block a controlled Pilot. Missing a trustworthy manual verification implementation does block view-reward acceptance. Phase 7 does not relabel a disabled provider as a working manual adapter or authorize fabricated counts. Firebase/legal/product boundary gaps are not solved by setting environment variables.

## Chosen topology

GitHub main commit → hosted CI acceptance → scanned/attested GHCR images + migration artifact → separately approved operator preparation → server pulls by digest → isolated V3 services. No server image builds, Windows tar-copy cycle, automatic deployment or Production promotion.

- Compose project `weymela-v3-pilot`, file [`docker/compose.pilot.yml`](../../docker/compose.pilot.yml).
- Services `weymela-v3-pilot-api`, `weymela-v3-pilot-worker`, `weymela-v3-pilot-web`, `weymela-v3-pilot-postgres`.
- Private `weymela-v3-pilot-data` (internal; DB/Worker/API); separate `weymela-v3-pilot-edge` (API/Web; API needs egress to Google's public signing certificate endpoint after live authorization).
- Only Web binds a host port: `127.0.0.1:18080`. API and PostgreSQL have **no host ports**. Worker no listener. Port observed unused, must recheck before deployment. API health uses DB and Worker readiness; no dependency cycle waiting for API before Worker starts.
- Proposed edge subnet `172.30.73.0/24`, Web proxy IP `.10`. This is a proposal, not a reservation. Recheck all host/VPN/Docker routes. API trusts only that approved Web proxy; do not trust V2 subnets or arbitrary forwarded headers.
- PostgreSQL has a NEW external volume `weymela-v3-pilot-postgres-data`, provisioned only after authorization. API cookie keys are a separate pre-provisioned protected bind mount. Compose will not silently create the key directory.
- Database `weymela_v3_pilot`, schema `v3`. Lowercase name is intentional: the approved startup guard requires `weymela_v3_pilot*`. It is the isolated equivalent of proposed `WeymelaV3PilotDb`, never `CreatorPayPilotDb`.
- All app containers non-root, read-only root, no added capabilities, no-new-privileges, bounded writable `/tmp`. No Docker socket mount, privileged mode, host networking, source mount or shared V2 volume.
- `restart: "no"` intentionally avoids automatic restart loops before bootstrap/readiness. A later authorized operational change may choose `unless-stopped` after acceptance. Nothing starts during Phase 7.

The preference to reuse PostgreSQL was evaluated: existing servers are attached only to V2-specific networks. Sharing would require new network access/DB administration on an existing cluster and introduces shared failure/resource boundaries. A separate 768 MiB-capped PostgreSQL instance is safer for the current no-V2-change constraint. Capacity approval remains mandatory; separate-host V3 Pilot is an alternative if coexistence cannot be made safe.

## Environment and image record

Safe templates: `docker/pilot.compose.env.example`, `docker/pilot.api.env.example`, `docker/pilot.worker.env.example`; generic `.env.example` remains. Populate only `/etc/weymela-v3/pilot/` after approval, not the repository. File mode 0600 (certificate read access explicitly granted only to API UID1654; do not assume Compose bind-file secret UID remapping changes host permissions). Cookie key directory UID1654, mode0700. Worker has its own DB credential and no cookie certificate/key mount. Database bootstrap secret only PostgreSQL, never API/Worker.

Required external inputs: approved GHCR digests; separate DB bootstrap/runtime/migrator/backup credentials; the four public Firebase Web values for `weymela-pilot`; the API-only read-only Firebase Admin signing JSON; the API-only Resend key and approved Pilot sender; auth-code/PIN secrets; cookie-protection private PFX/password/keyring; exact public origins; approved proxy/subnet; verified TLS edge; and user/legal/pricing bootstrap. Firebase ID-token verification itself still needs only public keys, but custom-token signing requires the separately mounted private API credential. No Android material belongs here.

Compose overrides `FinancialWritesEnabled=false` and `EnableDevelopmentIdentity=false`. Populating an env file cannot unfreeze it. No unfreeze endpoint exists. `ManualApproval` allows the real pending-deposit boundary, not fake money. Social disabled until trusted fallback exists. Push disabled; durable in-app notifications remain available. Rate multiplier1, 32KiB JSON, uploads unsupported; worker20 events/100 recipients/5 seconds. Retention remains preserve-only; do not invent nonfunctional retention env settings.

Use images `ghcr.io/<owner>/weymela-v3-{api,worker,web}:v3-<full-commit>-run<run-id>-attempt<attempt>`, consume `@sha256:` references from the complete manifest. No `latest`. Each CI image records pinned base digests, source commit, image ID, unpacked size, security report, SBOM and GitHub provenance attestation. Actual image sizes/scan results remain pending CI. PostgreSQL17 digest is independently reviewed and recorded before use.

## Proposed URLs and TLS edge (NOT configured)

- Canonical Web/API browser origin: `https://pilot.weymela.com`
- Transition-only V3 entry: `https://v3-pilot.weymela.com` redirects to the canonical origin.
- Server-side V3 API identity may remain `https://api-v3-pilot.weymela.com` while private service networking is prepared; browsers use same-origin `/api`.

The browser uses same-origin `/api`, proxied by V3 Web to API. Do **not** change it to direct cross-origin API calls: app cookies are `__Host-`, Secure/HttpOnly/SameSite=Strict. Separate API hostname can route `/api` and health through the same loopback V3 Web proxy if later needed; it does not provide an authenticated browser session automatically. No `VITE_API_URL` is needed by the current relative-URL Web application.

Later TLS edge must use new server blocks only, strict hostname routing, TLS certificate approval, HSTS `max-age=31536000`, no request URI/query/body/token logging. Overwrite `X-Forwarded-Proto` with the real TLS scheme and `X-Forwarded-For` with the actual client address (not appended untrusted chains), preserve Host. V3 Web relays sanitized values; API accepts only its Web proxy. Do not set `TlsEdgeConfirmed=true` before testing. No existing DNS/TLS/server blocks are edited by this package. For pre-DNS testing choose separately approved internal access and trusted HTTPS certificate; plain SSH HTTP tunneling alone cannot prove Secure-cookie/camera/PWA acceptance.

Web CSP/self, no framing, nosniff, no-referrer and `camera=(self), microphone=(), geolocation=(self), payment=(), usb=()` are supplied in the new Web image. API headers remain Phase 6. Production's existing policy is untouched. Offline assets only are cached; HTML/SW no-cache, hashed build assets immutable, API/QR/financial requests never cached or queued.

## Health, logging and operational limits

| Service | Health | Initial cap |
|---|---|---|
| API | `/health/live`, `/health/ready`; ready requires DB/migrations/effective pricing/trusted Admin/Worker success | 512MiB, 0.75CPU,128PIDs; connection pool20 |
| Worker | `dotnet Weymela.Worker.dll --check-health`; no HTTP port | 256MiB,0.25CPU,128PIDs; pool5 |
| Web | `/health/live`; `/health/ready` relays minimal API readiness | 96MiB,0.10CPU,128PIDs |
| PostgreSQL | `pg_isready` basic availability; API/Worker validate authenticated SQL/schema access | 768MiB,0.50CPU,40connections,128MiB shared buffers |

App readiness bootstrap is deliberate: a newly migrated empty DB is **not ready** until trusted Admin identity and approved effective financial configuration exist. Readiness never auto-seeds. Admin-only `/api/admin/operations` exposes outbox age/backlog, heartbeat and failure counters; `/api/admin/reconciliation` must report no mismatches. Never expose detailed diagnostics publicly.

All four services use Docker json-file `max-size=10m`, `max-file=3` (~120MiB total log ceiling). Metrics/log status only, no raw QR/auth tokens or proof secrets. PostgreSQL statement/error-statement logging is suppressed in the template to avoid financial/PII SQL text. Preserve accounting/legally required history; log limits do not authorize deleting DB records.

## Authorized future sequence — not executed

1. Choose GitHub owner/private repository; review inventory; explicitly authorize initial commit/push. Configure [CI protections](GITHUB-ACTIONS.md) before enabling registry publication.
2. Run hosted acceptance/release; inspect all three image digests, scan/attestation/SBOM and migration checksums. Archive complete release record outside expiring Actions storage.
3. Resolve capacity, adapter, legal and trusted bootstrap gates. Approve exact isolated host resources and separate secrets. No public DNS/cutover.
4. Only after separate deployment authorization: pull approved digests, prepare NEW volume/database/roles, verify pre-migration backup, apply reviewed bundle under migrator identity. Do not run a down migration. See [database runbook](V3-PILOT-DATABASE.md).
5. Start only V3 services, still frozen; verify real TLS/readiness/configuration and run [manual checklist](V3-PILOT-MANUAL-ACCEPTANCE.md). Financial test activation requires its own explicit approval; a prepared template is not permission to unfreeze.
6. On failure stop only named V3 API/Worker/Web (and V3 PG if approved); preserve data/keys/evidence; V2 remains serving exactly as before. No DNS replacement, no `down -v`, no global prune.

GitHub becomes V3 source of truth only after authorized initial push. Server becomes runtime + deployment-owned configuration, not an editing/build/sealing workstation. Production cutover/migration/mobile publication remains a future separately authorized phase.
