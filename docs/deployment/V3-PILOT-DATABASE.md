# Isolated V3 Pilot database / migration / recovery plan

**Plan only. No database, role, volume, migration, backup or restore was executed.** Do not connect to either existing PostgreSQL container. No old V2 migration is reused.

## Database and role isolation

New dedicated service `weymela-v3-pilot-postgres`, PostgreSQL17, new external volume `weymela-v3-pilot-postgres-data`, database `weymela_v3_pilot`, schema `v3`. The name satisfies the existing environment startup guard; `WeymelaV3PilotDb` does not. Port5432 is reachable only on private V3 data network. Runtime processes must not use bootstrap/superuser/owner credentials.

Prepare separate externally stored credentials for:

- `weymela_v3_bootstrap`: initial dedicated cluster administration only, never app credentials.
- `weymela_v3_migrator`: owns V3 schema/tables/functions/triggers and migration history; offline reviewed bundle only. Revoke public database CONNECT and public-schema CREATE; grant only intended roles. No login to V2 clusters.
- `weymela_v3_api`: V3 application DML only; no CREATEDB,CREATEROLE,SUPERUSER,BYPASSRLS,DDL,TRUNCATE or trigger-disabling privileges. SELECT/INSERT for append-only accounting/legal/evidence/audit tables, narrowly needed UPDATE on mutable aggregates/projections. No journal updates/deletes. Review exact grants against integration tests under runtime credentials before Pilot use.
- `weymela_v3_worker`: SELECT only necessary targeting/config/identity tables, bounded INSERT/UPDATE on outbox/notifications/checkpoints and guarded QR expiry; no financial posting table mutation privileges. Derive precise grants from `WorkerPump`, `OutboxProcessor`, `NotificationTargets`, not broad schema ownership.
- `weymela_v3_backup`: read-only access to V3 data needed by pg_dump. Restore uses a different separately authorized migrator/recovery identity.

Separate-role grant/provisioning scripts and trusted Admin/config bootstrap require reviewed values and an isolated rehearsal. The existing automated suite used temporary database-owner credentials; it does **not** prove least-privilege runtime grants. This is an explicit pre-deployment gate, not permission to grant ALL to applications. No user identity, legal acceptance, effective financial rates or wallet credit is seeded automatically.

## Verified source migration order

| Order | Migration | Scope |
|---|---|---|
|1|`20260911225904_InitialV3Schema`|New V3 financial/domain foundations |
|2|`20260911233032_AddViewRewardsQrAndPayouts`|Views,QR,verified sale,payout/settlement foundations |
|3|`20260912011149_AddOperationalSecurityAndNotifications`|Identity/public profiles,manual deposits,in-app delivery,Worker checkpoints,operational guards |

Phase7 adds **no migration**. All approved migration source/designer/helper/snapshot bytes remain unchanged. `tools/ci/migration-artifacts.sh` builds self-contained linux-x64 `efbundle`, idempotent forward SQL and checksummed metadata **on CI only**. Design-time factory uses an intentionally unusable localhost:1 address; artifact creation requires no database. Review complete SQL including PL/pgSQL trigger functions, role privileges and transactions before application. Existing destructive `Down` implementations are never part of image rollback.

A bundle is not a runtime image and is never auto-run by API/Worker. Archive its checksum with source commit, all image digests and ordered migration IDs. Confirm archive/attestation source matches the tested main commit.

## Before first migration — approved operator only

1. Resolve capacity and authorize NEW service/database/volume and migration target. Record environment/host/container/volume identity and financial freeze. No public listeners or V2 network attachments.
2. Before schema migration, make a custom-format backup of the new empty database, checksum and restore list. This preserves initial environment evidence; also archive the exact migration bundle/SQL/manifest. A brand-new empty DB does not excuse wrong-target checks.
3. Use protected `.pg_service.conf`/`.pgpass` files outside Git for PostgreSQL utilities. Example service `weymela_v3_pilot_backup` must identify only the new V3 host and DB. Verify `current_database()`, server address/port and version against the approved record; a matching database name alone is insufficient.

```sh
# FUTURE procedure only, from an authorized V3 maintenance context with PostgreSQL17 tools.
psql 'service=weymela_v3_pilot_backup' -X -v ON_ERROR_STOP=1 -Atc 'SELECT current_database(), inet_server_addr(), inet_server_port(), version();'
pg_dump --dbname='service=weymela_v3_pilot_backup' --format=custom --no-owner --no-acl --file=/approved/v3-backups/RELEASE-UTC-pre-migration.dump
sha256sum /approved/v3-backups/RELEASE-UTC-pre-migration.dump
pg_restore --list /approved/v3-backups/RELEASE-UTC-pre-migration.dump
```

Use a new unique0600 backup file, never overwrite an existing backup. Record checksum/list independently, encrypt and retain off-host. Do not store backups under source or reuse V2 backup jobs/paths. Separate V3 bootstrap role/config inventory and encrypted cookie-key recovery are also required; pg_dump does not back up roles or external files.

4. Execute verified `efbundle` only after target guard, reviewed grants/SQL and approval. **Current design-time factory does not read a deployment connection environment variable**: the EF bundle requires an explicit `--connection` override. Preferred secure invocation supplies a password-free connection (exact new host/database/migrator) and Npgsql's protected external password-file mechanism; verify that mechanism with the built bundle in an isolated rehearsal. Do not pass a credential-bearing string in arguments, enable verbose logs or use existing Pilot/Production credentials. If password-file authentication cannot be verified, stop and approve a secure one-shot migrator configuration adapter; do not assume an env file overrides the factory. Bundle behavior is documented by [Microsoft EF Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).
   Npgsql supports `Passfile=/protected/v3-migrator.pgpass` (or `PGPASSFILE`); see [Npgsql connection parameters](https://www.npgsql.org/doc/connection-string-parameters.html). A future password-free argument shape is `--connection 'Host=weymela-v3-pilot-postgres;Database=weymela_v3_pilot;Username=weymela_v3_migrator;Passfile=/run/secrets/v3-migrator.pgpass;Include Error Detail=false'` inside the approved isolated maintenance context. No actual password appears in that argument. Exact host identity and secret-file0600 permissions still need verification; this is not a command authorized to execute now.

5. Verify three rows of migration history, constraints/triggers and no pending model, then separately provision approved effective pricing and mapped trusted Admin. Start V3 frozen; Worker/readiness and reconciliation must pass. No general seed/Development identity workaround.

## Restore and rollback

**Destructive warning: never restore into existing Pilot/Production or overwrite V3 history without separate explicit approval.** Prefer a NEW empty isolated V3 replacement/rehearsal database, exact target guard, checksum verification, stop/freeze only approved V3 writers, preserve pre-restore evidence. No `--clean`, no drop database, no `docker compose down -v`.

```sh
pg_restore --dbname='service=weymela_v3_pilot_restore_rehearsal' --no-owner --no-acl --exit-on-error --single-transaction /approved/v3-backups/RELEASE-UTC-pre-migration.dump
```

After restore validate schema, runtime-role grants, totals/journal high-water marks, reconciliation, QR/idempotency/payout history, outbox and legal/identity records before any connection switch. A restore list alone is not a successful restore rehearsal. Owner must approve RPO/RTO, encryption/retention, recovery owner and off-host copy.

Normal side-by-side image failure rollback: stop only `weymela-v3-pilot-api`, `weymela-v3-pilot-worker`, `weymela-v3-pilot-web` using the V3 project definition; keep data/keys/volumes, optionally stop its own PG when safe. V2 traffic and DBs remain unchanged. First-deployment rollback is stopping V3; later image rollback requires previous digests plus forward-schema compatibility, never a down migration. No DNS cutover is part of Phase7.
