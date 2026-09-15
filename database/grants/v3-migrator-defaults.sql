\set ON_ERROR_STOP on

-- Required psql variable: backup_role.
-- Run while authenticated AS the V3 migration owner, not as API/Worker/backup.
-- Default privileges affect only objects this role creates in future migrations.
BEGIN;

-- PostgreSQL otherwise grants PUBLIC EXECUTE on newly created functions.
ALTER DEFAULT PRIVILEGES REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;

-- Preserve complete backup only for migration-owner-created persisted objects.
-- No future-object privileges are granted to API or Worker.
ALTER DEFAULT PRIVILEGES IN SCHEMA v3
    GRANT SELECT ON TABLES TO :"backup_role";
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT ON TABLES TO :"backup_role";
ALTER DEFAULT PRIVILEGES IN SCHEMA v3
    GRANT SELECT ON SEQUENCES TO :"backup_role";
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT ON SEQUENCES TO :"backup_role";

COMMIT;
