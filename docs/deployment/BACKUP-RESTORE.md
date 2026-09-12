# V3 backup / restore runbook — design only

Nothing in this runbook was executed against an existing Pilot/Production database. Do not reuse V2 credentials, database names, mounts or backups. Execute only after separately authorizing the exact new V3 environment and target.

## Backup prerequisites

Use PostgreSQL 17 client tools compatible with the target. A protected service file and `.pgpass`/secret mount supply credentials; never put passwords in command lines or logs. Use a read-only backup account where practical. Choose an explicit V3 database name (`weymela_v3_pilot_*` or `weymela_v3_prod_*`), verify server/database identity with `SELECT current_database(), inet_server_addr(), inet_server_port()`, and compare it to the approved environment record. An arbitrary matching name alone does not authorize a target.

Record release digest, migration list, database identity, UTC timestamp, expected financial freeze state and journal high-water marks. Use restrictive permissions on a NEW empty backup directory, outside the repository and with sufficient free space. Backup files contain sensitive data; encrypt them and retain a verified off-host copy under separately managed keys.

Example command shape, with concrete approved paths substituted by the operator:

```sh
pg_dump --dbname='service=weymela_v3_pilot_backup' --format=custom --no-owner --no-acl --file=/approved/v3-backups/RELEASE-UTC.dump
sha256sum /approved/v3-backups/RELEASE-UTC.dump
pg_restore --list /approved/v3-backups/RELEASE-UTC.dump
```

Save checksum and restore list alongside a manifest with no credentials. A restore list is structural validation, not proof of restorability. Restore into a separately authorized **new empty isolated V3 rehearsal database**, run migrations/readiness and accounting reconciliation, record table counts and results. Do not direct rehearsal at an existing Pilot/Production DB. Do not remove prior backups. Owner must choose RPO/RTO and retention; suggested initial objective is daily backup plus pre-migration backup, subject to measured backup/restore duration.

## Restore — destructive operation requiring separate approval

**STOP before restore. Restoring to an existing database can overwrite history and is not a normal rollback.** Require two-person confirmation of environment, host, explicit database, dump checksum, timestamp, release and loss window. Freeze writes, stop only the approved V3 API/Worker, preserve a fresh pre-restore backup. Never use an unresolved variable/glob, V2 database name, broad delete, or automatic `--clean` command.

Prefer a new empty approved V3 replacement database and a controlled configuration switch after validation:

```sh
pg_restore --dbname='service=weymela_v3_pilot_restore_rehearsal' --no-owner --no-acl --exit-on-error --single-transaction /approved/v3-backups/RELEASE-UTC.dump
```

This example intentionally supplies neither create/drop commands nor credentials. Restore data-protection key backups only to the matching environment, never between Pilot and Production. Keep financial writes disabled until migration version, reconciliation, access, health, Worker backlog and business totals match the approved recovery record. Image rollback requires known versioned digests and schema compatibility; never run a destructive down migration to make an older image fit. Archive all approval/evidence records.
