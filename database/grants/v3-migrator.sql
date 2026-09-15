\set ON_ERROR_STOP on

-- Required psql variables: migrator_role, database_name.
-- Run with the reviewed maintenance identity authorized to grant on this
-- database and its schemas. Stop if it lacks authority; do not add role
-- membership or transfer ownership to work around a grant failure. The
-- migrator already owns v3 and its migration objects.
BEGIN;

REVOKE ALL PRIVILEGES ON DATABASE :"database_name" FROM :"migrator_role";
GRANT CONNECT ON DATABASE :"database_name" TO :"migrator_role";
REVOKE ALL PRIVILEGES ON SCHEMA public, v3 FROM :"migrator_role";
GRANT USAGE, CREATE ON SCHEMA public, v3 TO :"migrator_role";

-- The EF history table and migration-created V3 objects stay owned by this role.
-- Ownership permits approved DDL and history maintenance without SUPERUSER.
COMMIT;
