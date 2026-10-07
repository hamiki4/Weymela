# Private receipt recovery and Pilot financial-test preparation

This is a source runbook, not authorization to deploy, migrate, enable financial writes, or alter Production. Weymela's existing private server-storage and server-local backup pattern is approved for this release. There is no new storage provider or encryption-key infrastructure.

## Storage and recovery boundary

Historical Production V2 runs an API-only Docker volume `creatorpay-prod-deposit-proofs` mounted at `/app/data/deposit-proofs`; an older V3 integration Pilot uses the same API-only named-volume pattern. Current Weymela V3 already uses the equivalent persistent-server pattern with a dedicated API-only host bind: Pilot `/var/lib/weymela-v3/pilot/receipts` to `/run/weymela-v3/receipts`. This directory must be `1654:1654`, mode `0700`; receipt files are `0600`. Web and Worker receive no receipt mount. Keep the current opaque `ProofReference`, private authorization, JPEG/PNG and 4 MiB limit. Production's V3 path is separate and is **not** changed here.

The Pilot host directory and API mount are not provisioned in the live Pilot yet. Before a later deployment, the operator must create only this dedicated Pilot host directory with the required owner/mode, check that it is a real directory, render Compose, verify the API-only bind and disabled host-path auto-creation, then run a non-sensitive write/read/delete fixture inside the API container. Do not create a real deposit during mount verification.

Existing Pilot PostgreSQL backups are server-local. No approved off-host receipt backup destination/key or paired receipt job was found. Their absence is **not** a new release gate. Server-local backups cannot recover from loss or compromise of the host; record that disaster-recovery limit honestly. The optional [encrypted off-host tool](../../tools/ops/receipt-backup.py) from commit `49768853` remains available if an approved destination/key is established later. Its isolated fixture proves only tool mechanics, not live receipt recovery. Do not generate an ad hoc key or introduce a new provider.

## Required paired local backup before migration or financial testing

1. Record release commit/image digests, exact Pilot database identity, migration baseline, UTC timestamp, expected loss window and journal high-water marks. Confirm sufficient capacity in the existing protected Pilot backup area. Use PostgreSQL **17** `pg_dump` and `pg_restore` (the Pilot PostgreSQL 17 container has compatible tools if the host does not). Keep credentials outside source, command lines and logs.
2. In a separately authorized maintenance window, stop only the Pilot V3 API and Worker and verify both remain stopped. This freezes both new receipt files and database `ProofReference` rows. The filesystem and database have no shared transaction.
3. Create a fresh unique custom-format Pilot database dump in a `0700` protected directory as a `0600` file. Record SHA-256 and run `pg_restore --list`. Never substitute an old dump. Preserve migration history and the exact database identity in the backup record.
4. While writers remain stopped, inventory `/var/lib/weymela-v3/pilot/receipts`. Reject symlinks, unexpected names and wrong ownership/modes. Valid receipt names are `r_` plus 64 lowercase hexadecimal characters. Make a protected local archive preserving opaque names and numeric ownership/modes; record each file's size and SHA-256, archive SHA-256 and UTC snapshot time in a protected manifest beside the matching dump. A zero-receipt archive is valid only when the matching database has no receipt references.
5. Restore the database dump into a **new isolated** PostgreSQL database and the receipt archive into a **new isolated** private directory. Verify dump list, restored checksums, `1654:1654`/`0700`/`0600`, `v3-verify.sql`, financial reconciliation, and that restored non-null `DepositRequests.ProofReference` keys match restored files. Do not overwrite live files or restore to the live database. Record the paired restore result and retain the backup set under the existing protected server-local policy. Resume only the approved Pilot services in their prior financial-write state.

A database restore without corresponding receipt evidence is incomplete. A stopped-writer paired capture gives a consistent operational point; it does not claim atomicity between PostgreSQL and the filesystem. If either source changes during capture, discard that candidate pair and repeat. Receipt files are retained **until an approved financial-record retention policy is defined**; no automatic deletion period is introduced.

If an approved off-host destination/key later exists, the existing optional tool can encrypt the pair, upload with pinned SSH host key, read it back, compare SHA-256, and verify an isolated restore. That is an enhancement to disaster recovery, not a prerequisite imposed by this release.

## Pilot financial-write authorization modes

Normal Pilot defaults to `V3_PILOT_FINANCIAL_WRITES_ENABLED=false`, `V3_PILOT_FINANCIAL_WRITES_MODE=Disabled` and `V3_PILOT_FINANCIAL_WRITES_UNTIL_UTC=disabled` in Compose. API and Worker receive the same values. Runtime startup, the protected environment assembler and Compose preflight reject mismatches or ambiguous activation. `Timed` mode requires a UTC end strictly in the future and no more than four hours away. Durable acceptance testing uses explicit `Uat` mode with the UTC end set to `disabled`. Both modes are Pilot-only and require `ManualApproval`; Production remains denied by runtime validation and Production Compose/config is unchanged. Journal, idempotency, reconciliation, reviewer authority and database guards remain in force.

Activation sequence: complete the paired backup/restore checks, verify the receipt mount, grants, reconciliation and healthy API/Worker. Set the same enabled state and mode for API and Worker through protected Pilot configuration, rerun preflight, restart only Pilot API/Worker, verify effective state and execute the named manual test. `Timed` uses a bounded `YYYY-MM-DDTHH:MM:SSZ` end; `Uat` uses `disabled`. Reconcile after test actions. Deactivate by setting `false`/`Disabled`/`disabled`, rerunning preflight, restarting only Pilot services, proving financial mutations blocked again and reconciling. Never edit wallets directly or bypass triggers.

## Historical three-migration Pilot precheck (completed)

Pilot is now at 24 migrations. The following 21-to-24 procedure is retained as historical evidence and must not be repeated on Pilot.

The reviewed order is `20260928213157_AlignDepositReviewAuthority`, `20260928230108_AddCreatorNumbers`, `20260929022846_BindUgcSaleAssignments`. Do **not** apply them in this source-preparation task. The immutable release package now carries checksummed `migrations/grants/baseline-21/v3-verify.sql` from installed commit `7d537bb8a83ed2757a2149261b8dd7a449f51629` and the exact target grant scripts plus verifier under `migrations/grants/current/`. `migration-manifest.json` binds both grant stages to their source commits and migration counts; `verify-release.py` rejects missing, altered or mismatched grant SQL. Never substitute SQL from a mutable server checkout.

For a later separately authorized Pilot deployment, use this order:

1. Verify the complete release package and its SHA-256 inventory, image digests, Pilot target identity, financial-write freeze, host/Compose preflight, and actual migration history. Require exactly 21 installed migrations ending at `RetireSupportSessions`. Run **only the packaged baseline-21 verifier** against the current database. Do not run the target verifier against old grants.
2. Create and validate the fresh paired PostgreSQL/receipt backup above, including `pg_restore --list` and both checksums. Restore the pair to an isolated copy for the full upgrade rehearsal before touching live Pilot.
3. On the isolated copy, apply only the three reviewed migrations under the protected migrator. Install the packaged target grants in the reviewed API, Worker, Migrator, Backup and Migrator-defaults order using their designated owner identities. Verify 24 exact migration IDs, Creator-number backfill, UGC assignment constraints, no pending model changes, reconciliation, receipt keys and **the packaged target verifier**. Keep runtime roles without ownership or `DELETE`.
4. Only after the rehearsal and a separate live authorization, repeat the migration and grant steps against Pilot from the same verified artifact. Run the target verifier after grants, before starting new images. Deploy only the manifest's immutable API/Web/Worker digests. Recheck readiness, grants, reconciliation, financial freeze and safe smoke.

If any gate fails, stop; do not run a newer verifier on the old state, grant around it manually, or run routine `Down` migrations. Rolling back `AddCreatorNumbers` loses numeric IDs assigned later; prefer a controlled full paired restore when rollback is necessary, with an explicit loss-window decision.

## UGC capacity and Creator-photo release precheck (24 to 26)

The next reviewed schema changes are `20260929203557_AddUgcPlatformCapacities` and then `20260930031549_AddCreatorProfilePhotos`. The release package must contain the baseline-24 verifier from deployed source `47e63df0b71be941922ff9b316e3a0a0466ab187`, with SHA-256 `ef06c1b2690ba02db3329027c49b4286684ff9e6e898225cba9d0c82e9c287b0`, and the checksummed target grants and verifier. Require exactly 24 installed migrations and run that baseline verifier before changes. Create a fresh protected PostgreSQL/receipt/Creator-photo backup, rehearse both migrations and grants on an isolated restored copy, and verify the 26 exact IDs, nullable legacy UGC request bindings, capacity constraints, Creator-only photo-key constraint, no photo backfill, and narrow target grants. Only a later separately authorized deployment may apply the packaged migrations and grants to Pilot. Both `Down` migrations refuse when attributed activity or photo references would be lost; use the reviewed paired restore procedure if reversal becomes necessary. Keep financial writes off.

Existing published UGC opportunities keep their original global Creator capacity and posting requirements; the migration does not infer platform slots or assign platforms to historical requests. New and revised Business drafts use explicit per-platform slots when Creator posting is required. Delivery-only drafts retain a separate general Creator count and require no social-platform assignment. Pending requests consume no slots; approvals consume one available slot within the original request and assignment transaction.

## Other readiness work

Live Firebase Delete Entire Account reconciliation remains a later Pilot-only test using a dedicated disposable identity. Do not delete a real user. Production Worker Docker `json-file` log rotation remains a separate change; the documented recommendation is `max-size: 10m`, `max-file: 3`. Do not modify Production in this task.

## Image-upload transport correction

The canonical Pilot host edge currently forwards to Web on loopback `18080` and
has Nginx's default 1 MiB request limit. Web previously limited every request to
32 KiB. Both layers must permit the existing API image contract: JPEG/PNG files
up to 4 MiB, with bounded multipart overhead. The immutable Web image now allows
5 MiB only on `/api/business/deposit-requests` and `/api/creator/photo`. Private
review uploads receive a separate 101 MiB transport envelope only on
`/api/creator/creator-budgets/{id}/content/review` and
`/api/creator/ugc/assignments/{id}/submit-review`; they stream through both
proxies without request buffering. Ordinary API routes retain 32 KiB. API file
validation remains authoritative at 4 MiB for images, 10 MiB for review images,
and 100 MiB for review video.

At the next separately authorized Pilot deployment, back up the active canonical
HTTPS site and install the reviewed `pilot-image-uploads.nginx.conf` snippet from
the exact release source commit inside that server. Verify the source checksum,
run `nginx -t`, and reload the edge only under deployment authorization. The
snippet inherits the site's trusted forwarding headers; do not install it in
Production or substitute the historical V2/V3 integration template. The generic
V3 `/api/` route must remain a plain prefix so the bounded upload regex locations
can take precedence; V2's `/api/v1/` route remains `^~`. Do not raise global
limits. Verify a 65,752-byte and a 4 MiB valid receipt traverse both proxies while
financial writes are OFF and return API `FinancialWritesPaused`, a review body
above 32 KiB reaches the API rather than the ordinary proxy limit, and oversized
requests fail with a bounded JSON 413. Do not open the financial window until the
restricted-role Admin Wallet/Reports checks also pass.

Pilot has 29 migrations at deployed source
`7c70fc8f88a8e3b294ca5f8af466845da1c91151`. For the financial-UAT release,
require that exact history and run the packaged baseline-29 verifier
(SHA-256 `a688a6ce9bbd5cc9bd5fbf74ce80b13e869140810dabbe6bc9dbe8c8c613eb5a`)
before any change. Back up PostgreSQL and every private durable-media directory,
rehearse on the restored isolated copy, and apply only
`20261007041919_AlignFinancialUatFlows`. Install the packaged target grants,
require all 30 exact migrations, and run only the packaged target verifier before
starting the new images. Never substitute a verifier from a mutable checkout or
reapply an installed migration.
