# Private receipt recovery and Pilot financial-test preparation

This is an operator runbook, not authorization to deploy, migrate, unfreeze financial writes, or alter Production. The paired-backup tool is [`tools/ops/receipt-backup.py`](../../tools/ops/receipt-backup.py). Its fixture mode proves the encryption and restore mechanics; it is **not** an off-host backup or a PostgreSQL restore rehearsal.

## Current state and prerequisites

The committed Pilot Compose template binds `/var/lib/weymela-v3/pilot/receipts` only into the API at `/run/weymela-v3/receipts`. Production's intended, separate path is `/var/lib/weymela-v3/production/receipts`. Both require API UID/GID `1654:1654`, directory `0700`, receipt files `0600`, no symlinks, and no public serving. The live Pilot API inspected on 2026-09-29 has **no receipt bind mount**, and the Pilot host receipt directory does not yet exist. Do not create or mount it as part of a backup test without deployment authorization.

Existing Pilot PostgreSQL dumps are local. No approved off-host destination, encryption public key, or paired receipt backup job was identified in the repository or inspected Pilot backup-directory inventory. The encrypted off-host transfer is implemented as a pinned-host-key SFTP/SCP upload followed by a full read-back checksum, but cannot run until an owner supplies a private destination, a dedicated SSH identity, pinned host key, and an approved GPG recipient fingerprint. Do not reuse an unrelated SSH key or copy private decryption keys to the application host. The remote directory must be pre-provisioned private and non-public; its operator must control access and backup retention. The local GPG home and backup output directory must be `0700`; SSH identity `0600`. All credentials and private keys remain outside Git and logs.

The approved financial evidence backup is **one encrypted archive** containing a PostgreSQL custom-format dump and the flat opaque receipt files, with an internal manifest of their filenames, UTC creation time, sizes, and SHA-256 hashes. The ciphertext has a separate SHA-256 sidecar. The tool refuses symlinks, unexpected/transient receipt names, wrong owner/mode, oversized files, missing PostgreSQL `pg_restore --list` validation, an unapproved source path, and a production backup without explicit stopped-writer confirmation. It uploads the ciphertext off-host and downloads it again to compare SHA-256 before reporting success. `pg_restore` from PostgreSQL 17 is required in the protected backup context; the host inspected here does not currently have it. Encryption is GPG public-key encryption; only the independent recovery context should hold the private key.

## Make a consistent backup set — future authorized window

1. Record exact environment, release commit/image digests, migration history, PostgreSQL server/database identity, UTC time, approved loss window, financial freeze state, and journal high-water marks. Confirm sufficient protected local and off-host capacity. Check the backup account is read-only and uses external service/pass files. Never put passwords in commands or logs.
2. In a separately approved maintenance window, stop **only** the Pilot V3 API and Worker and verify they remain stopped. This prevents new `ProofReference` rows or receipt files while the pair is captured. A dynamic configuration flag alone is not a filesystem/database snapshot. Do not stop Production or V2.
3. Create a fresh unique `0600` PostgreSQL 17 custom-format dump in a protected `0700` directory, using the exact Pilot backup service. Run `sha256sum` and `pg_restore --list` on that dump. Verify the server/database identity before dumping. Keep the writers stopped. Do not use a prior dump and a later receipt directory as if they were one recovery point.
4. Run the paired tool with explicit approved paths and key fingerprint. The arguments below are a **shape**, not live credentials or authorization:

   ```sh
   python3 tools/ops/receipt-backup.py backup \
     --environment pilot \
     --receipts /var/lib/weymela-v3/pilot/receipts \
     --db-dump /approved/protected/new-pilot.dump \
     --output-dir /approved/protected/backup-sets \
     --gpg-home /approved/protected/public-keyring \
     --recipient APPROVED_FULL_FINGERPRINT \
     --writers-stopped \
     --offhost-user APPROVED_BACKUP_USER \
     --offhost-host APPROVED_BACKUP_HOST \
     --offhost-dir /approved/private/pilot \
     --ssh-key /approved/protected/backup-ssh-key \
     --known-hosts /approved/protected/backup-known-hosts
   ```

5. Record the tool's ciphertext size, SHA-256, receipt count, paired database dump SHA-256, upload/read-back result, start/end UTC, and protected off-host location in the operations record. Retain the local dump/checksum until the recovery owner confirms the off-host set; do not delete prior backups as part of this procedure. Only then resume the approved services in their prior financial-write state.

This procedure obtains consistency by stopping writers across **both** captures. PostgreSQL and the receipt filesystem have no shared transaction and there is no claimed cross-system atomic snapshot. If the stop or source-stability check fails, discard the candidate set and repeat the complete pair. A database restore without matching receipt evidence is incomplete.

## Verify a restore without touching live data

Retrieve the encrypted archive from the protected off-host target into a new private staging directory. Compare its SHA-256 with the recorded checksum, then decrypt using the recovery key in an isolated recovery context. The tool permits only a **new** restore directory outside live Weymela paths; it does not extract symlinks or arbitrary paths:

```sh
python3 tools/ops/receipt-backup.py verify-restore \
  --archive /approved/protected/backup-sets/EXACT.receiptset.gpg \
  --expected-sha256 EXACT_SHA256 \
  --restore-dir /approved/isolated/new-verification-directory \
  --gpg-home /approved/protected/recovery-keyring
```

The verifier compares every restored byte count and SHA-256 against the encrypted internal manifest and sets receipt directory/file owner to `1654:1654`, modes `0700`/`0600`. It leaves `database.dump` for an independently authorized restore into a **new empty isolated** PostgreSQL database. Check `pg_restore --list`, restore the dump, run `database/grants/v3-verify.sql` and financial reconciliation, compare all non-null `DepositRequests.ProofReference` opaque keys with restored receipt filenames, and verify a representative image through an authorized private read path. Preserve the manifest/checksum and results as protected evidence. Remove only the isolated verification restore after review; never overwrite or delete live receipts. An actual environment replacement requires separate approval, protected pre-restore backup, and a controlled switch.

Receipt files are **retained until an approved financial-record retention policy is defined**. This procedure creates no automatic receipt deletion or retention period.

## Current Pilot financial-write guards and later activation

Pilot is frozen at multiple layers:

- The API and Worker in `docker/compose.pilot.yml` explicitly set `V3__FinancialWritesEnabled: "false"`; an env-file edit cannot override those entries. The protected API env assembler in `tools/ops/pilot-runtime.py` requires `false`, and `tools/ci/pilot-preflight.py` requires both services frozen.
- `RuntimeOptions.Load` rejects `FinancialWritesEnabled=true` outside Development, including Pilot. The API middleware returns `FinancialWritesPaused` for classified financial mutations. There is no unfreeze endpoint. `V3__Deposits__Mode=ManualApproval` is already the Pilot provider setting, but it does not credit a wallet on receipt submission.
- Platform Admin's `/api/admin/reconciliation` is read-only and must return no mismatches before and after controlled tests. Existing journal, trigger, idempotency, approval, and role guards remain authoritative.

Changing one environment value **cannot** activate manual Pilot testing. A separately reviewed, Pilot-only activation patch must: (1) permit the flag only for `Pilot` with an explicit protected approval marker and startup validation; keep `Production` denied; (2) make the Pilot Compose API and Worker flags explicit, default-false controlled values, and update the protected assembler/preflight/tests accordingly; (3) require the approved backup/restore evidence, applied migrations, receipt mount, healthy Worker/readiness, financial reconciliation, and a named test window before restart; (4) verify the effective value after restart without exposing secrets. Only then may an authorized operator change the protected Pilot configuration and recreate the affected Pilot services. Reversal is the same controlled setting back to `false` plus a Pilot-only restart. Run reconciliation before activation, after each receipt approval/rejection and funding/checkout exercise, and after refreeze. Never edit wallet balances directly or disable triggers. Do not change Production configuration. No activation code/config change is made in this phase.

## Three-migration Pilot precheck

The new migrations in the committed source, in chronological order, are:

1. `20260928213157_AlignDepositReviewAuthority` — expands the existing guarded deposit review to active Operations Admin as well as Platform Admin.
2. `20260928230108_AddCreatorNumbers` — backfills stable numeric Creator IDs ordered by existing `PublicId`, then uses a PostgreSQL sequence/trigger for new Creators.
3. `20260929022846_BindUgcSaleAssignments` — adds nullable exact UGC assignment attribution and source-binding guards; historical ambiguous rows remain null.

Before any later Pilot migration: confirm the **actual** `__EFMigrationsHistory` baseline and target identity; archive a fresh protected Pilot custom-format DB dump and successful `pg_restore --list`; capture and verify the matching encrypted off-host receipt set; verify the release manifest commit, migration order, checksums of bundle/forward SQL and source, and image digests; run `v3-verify.sql` and reconciliation; rehearse the exact cumulative forward SQL and grants against a restored isolated Pilot copy. Apply the reviewed bundle only with the protected migrator through `tools/ops/run-v3-migrations.py` after separate approval. Then re-run history, grants, `v3-verify.sql`, reconciliation, receipt-key checks, and readiness before financial activation. Never run a `Down` migration on Pilot as routine rollback. `AddCreatorNumbers` down removes the numeric column/sequence and loses IDs assigned after migration; where rollback is needed, prefer restoring the complete protected pre-migration database/receipt pair into an approved replacement environment, with an explicit loss-window decision. Pilot migrations are **not applied** by this document.

## Pilot-only external identity deletion rehearsal

After the new image is separately deployed, identify a **dedicated disposable Pilot identity** with no real user's data or unresolved financial obligations. If none exists, stop without creating/deleting a real user. Record its normalized email and current Firebase/Pilot-local binding in a protected test record. A real Platform Admin, without View As, requests `Delete Entire Account` with reason and `DELETE`; confirm all roles become inaccessible immediately and the `AccountIdentityDeletion` outbox item is processed by the API's Firebase Admin adapter. Verify external Firebase deletion succeeds, the local identifier is released **after** that success, email can enter a new Create Account flow, and retained journal/audit/financial rows reconcile. Exercise an idempotent retry and capture failure/retry behavior without exposing credentials or PII. This remains an unexecuted Pilot test.

## Production Worker log rotation — recommendation only

Read-only Docker inspection on 2026-09-29 found the existing Production Worker uses `json-file` with **no** `max-size` or `max-file`. In a separately approved Production maintenance change, add service-level Docker logging limits such as `max-size: "10m"` and `max-file: "3"` to the Production Worker's managed Compose definition, then recreate only that service so the new container receives the limits. Verify `docker inspect` afterward and retain logs according to the approved operational policy. Do not truncate its current log or change Production in this phase.
