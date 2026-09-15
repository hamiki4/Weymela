\set ON_ERROR_STOP on

-- Required psql variables: api_role, worker_role, database_name.
-- Run as the database owner after v3-api.sql and v3-worker.sql. This verifier
-- compares every V3 table/column/routine privilege and rejects any extra access.

BEGIN;

CREATE TEMP TABLE _v3_runtime_roles (
    role_name name PRIMARY KEY,
    role_kind text NOT NULL CHECK (role_kind IN ('api', 'worker'))
) ON COMMIT DROP;
INSERT INTO _v3_runtime_roles VALUES
    (:'api_role', 'api'),
    (:'worker_role', 'worker');

CREATE TEMP TABLE _v3_database_name (database_name name NOT NULL) ON COMMIT DROP;
INSERT INTO _v3_database_name VALUES (:'database_name');

CREATE TEMP TABLE _v3_expected_tables (
    role_kind text NOT NULL,
    schema_name name NOT NULL,
    table_name name NOT NULL,
    can_select boolean NOT NULL DEFAULT false,
    can_insert boolean NOT NULL DEFAULT false,
    can_update boolean NOT NULL DEFAULT false,
    PRIMARY KEY (role_kind, schema_name, table_name)
) ON COMMIT DROP;

INSERT INTO _v3_expected_tables
    (role_kind, schema_name, table_name, can_select, can_insert, can_update)
VALUES
    ('api', 'public', '__EFMigrationsHistory', true, false, false),
    ('api', 'v3', 'AuthIdentifiers', true, true, true),
    ('api', 'v3', 'EmailAuthChallenges', true, true, true),
    ('api', 'v3', 'IdentityBindings', true, true, false),
    ('api', 'v3', 'CommercePermissions', true, true, false),
    ('api', 'v3', 'PublicWorkspaceProfiles', true, true, false),
    ('api', 'v3', 'RoleEnrollments', true, true, true),
    ('api', 'v3', 'BusinessWallets', true, true, false),
    ('api', 'v3', 'CustomerCashbackAccounts', true, true, false),
    ('api', 'v3', 'LegalDocumentVersions', true, false, false),
    ('api', 'v3', 'LegalAcceptances', true, true, false),
    ('api', 'v3', 'AuthorizedDevices', true, true, true),
    ('api', 'v3', 'DeviceSessions', true, true, true),
    ('api', 'v3', 'IdempotencyRecords', true, true, false),
    ('api', 'v3', 'AuditEvents', false, true, false),
    ('api', 'v3', 'OutboxMessages', true, true, false),
    ('api', 'v3', 'InAppNotifications', true, true, false),
    ('api', 'v3', 'WorkerCheckpoints', true, false, false),
    ('api', 'v3', 'FinancialConfigurationVersions', true, false, false),
    ('api', 'v3', 'FinancialJournals', true, false, false),
    ('api', 'v3', 'FinancialJournalLines', true, false, false),
    ('api', 'v3', 'CustomerCashbackEntries', true, false, false),
    ('api', 'v3', 'PayoutRecords', true, false, false),
    ('api', 'v3', 'Promotions', true, false, false),
    ('api', 'v3', 'PricingSnapshots', true, false, false),
    ('api', 'v3', 'CreatorAllocations', true, false, false),
    ('api', 'v3', 'CreatorPromotionParticipations', true, false, false),
    ('worker', 'v3', 'WorkerCheckpoints', true, true, true),
    ('worker', 'v3', 'FinancialConfigurationVersions', true, false, false),
    ('worker', 'v3', 'OutboxMessages', true, true, true),
    ('worker', 'v3', 'CommercePermissions', true, false, false),
    ('worker', 'v3', 'InAppNotifications', true, true, false),
    ('worker', 'v3', 'OfferQrSessions', true, false, false),
    ('worker', 'v3', 'Promotions', true, false, false),
    ('worker', 'v3', 'PricingSnapshots', true, false, false),
    ('worker', 'v3', 'CreatorAllocations', true, false, false),
    ('worker', 'v3', 'PayoutRecords', true, false, false),
    ('worker', 'v3', 'DepositRequests', true, false, false);

CREATE TEMP TABLE _v3_expected_update_columns (
    role_kind text NOT NULL,
    schema_name name NOT NULL,
    table_name name NOT NULL,
    column_name name NOT NULL,
    PRIMARY KEY (role_kind, schema_name, table_name, column_name)
) ON COMMIT DROP;
INSERT INTO _v3_expected_update_columns VALUES
    ('api', 'v3', 'InAppNotifications', 'ReadAtUtc'),
    ('api', 'v3', 'InAppNotifications', 'Version'),
    ('worker', 'v3', 'InAppNotifications', 'PushState'),
    ('worker', 'v3', 'InAppNotifications', 'PushAttempts'),
    ('worker', 'v3', 'InAppNotifications', 'NextPushAtUtc'),
    ('worker', 'v3', 'InAppNotifications', 'LastPushErrorCode'),
    ('worker', 'v3', 'InAppNotifications', 'Version'),
    ('worker', 'v3', 'OfferQrSessions', 'Status'),
    ('worker', 'v3', 'OfferQrSessions', 'Version');

CREATE TEMP TABLE _v3_expected_functions (
    role_kind text NOT NULL,
    function_name name NOT NULL,
    identity_arguments text NOT NULL,
    PRIMARY KEY (role_kind, function_name, identity_arguments)
) ON COMMIT DROP;
INSERT INTO _v3_expected_functions VALUES
    ('api', 'check_wallet_journal', ''),
    ('api', 'check_earned_account', ''),
    ('api', 'guard_notification_identity', ''),
    ('worker', 'guard_outbox_envelope', ''),
    ('worker', 'guard_notification_identity', ''),
    ('worker', 'guard_qr_use', '');

DO $verify$
DECLARE
    runtime record;
    relation record;
    column_row record;
    routine record;
    expected record;
    role_oid oid;
    actual boolean;
    expected_value boolean;
BEGIN
    IF current_database() <> (SELECT database_name FROM _v3_database_name) THEN
        RAISE EXCEPTION 'Grant verifier connected to unexpected database';
    END IF;

    FOR runtime IN SELECT * FROM _v3_runtime_roles LOOP
        SELECT oid INTO role_oid FROM pg_roles WHERE rolname = runtime.role_name;
        IF role_oid IS NULL THEN
            RAISE EXCEPTION 'Required runtime role % does not exist', runtime.role_name;
        END IF;
        IF EXISTS (
            SELECT 1 FROM pg_roles
            WHERE oid = role_oid AND (rolsuper OR rolcreaterole OR rolcreatedb OR rolreplication OR rolbypassrls)
        ) THEN
            RAISE EXCEPTION 'Runtime role % has prohibited role attributes', runtime.role_name;
        END IF;
        IF EXISTS (SELECT 1 FROM pg_auth_members WHERE member = role_oid) THEN
            RAISE EXCEPTION 'Runtime role % has unexpected role membership', runtime.role_name;
        END IF;
        IF EXISTS (SELECT 1 FROM pg_database WHERE datdba = role_oid)
            OR EXISTS (SELECT 1 FROM pg_namespace WHERE nspowner = role_oid)
            OR EXISTS (SELECT 1 FROM pg_class WHERE relowner = role_oid)
            OR EXISTS (SELECT 1 FROM pg_proc WHERE proowner = role_oid) THEN
            RAISE EXCEPTION 'Runtime role % owns database objects', runtime.role_name;
        END IF;

        IF NOT has_database_privilege(runtime.role_name, current_database(), 'CONNECT')
            OR has_database_privilege(runtime.role_name, current_database(), 'CREATE')
            OR has_database_privilege(runtime.role_name, current_database(), 'TEMPORARY') THEN
            RAISE EXCEPTION 'Runtime role % has unexpected database privileges', runtime.role_name;
        END IF;
        IF NOT has_schema_privilege(runtime.role_name, 'v3', 'USAGE')
            OR has_schema_privilege(runtime.role_name, 'v3', 'CREATE')
            OR has_schema_privilege(runtime.role_name, 'public', 'CREATE')
            OR has_schema_privilege(runtime.role_name, 'public', 'USAGE') <> (runtime.role_kind = 'api') THEN
            RAISE EXCEPTION 'Runtime role % has unexpected schema privileges', runtime.role_name;
        END IF;

        FOR relation IN
            SELECT n.nspname AS schema_name, c.relname AS table_name
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p')
              AND n.nspname IN ('public', 'v3')
        LOOP
            SELECT * INTO expected FROM _v3_expected_tables
            WHERE role_kind = runtime.role_kind AND schema_name = relation.schema_name
              AND table_name = relation.table_name;
            actual := has_table_privilege(runtime.role_name,
                format('%I.%I', relation.schema_name, relation.table_name), 'SELECT');
            IF actual <> COALESCE(expected.can_select, false) THEN
                RAISE EXCEPTION 'Unexpected SELECT privilege for % on %.%', runtime.role_name, relation.schema_name, relation.table_name;
            END IF;
            actual := has_table_privilege(runtime.role_name,
                format('%I.%I', relation.schema_name, relation.table_name), 'INSERT');
            IF actual <> COALESCE(expected.can_insert, false) THEN
                RAISE EXCEPTION 'Unexpected INSERT privilege for % on %.%', runtime.role_name, relation.schema_name, relation.table_name;
            END IF;
            actual := has_table_privilege(runtime.role_name,
                format('%I.%I', relation.schema_name, relation.table_name), 'UPDATE');
            IF actual <> COALESCE(expected.can_update, false) THEN
                RAISE EXCEPTION 'Unexpected table UPDATE privilege for % on %.%', runtime.role_name, relation.schema_name, relation.table_name;
            END IF;
            IF has_table_privilege(runtime.role_name,
                format('%I.%I', relation.schema_name, relation.table_name), 'DELETE')
                OR has_table_privilege(runtime.role_name,
                    format('%I.%I', relation.schema_name, relation.table_name), 'TRUNCATE')
                OR has_table_privilege(runtime.role_name,
                    format('%I.%I', relation.schema_name, relation.table_name), 'REFERENCES')
                OR has_table_privilege(runtime.role_name,
                    format('%I.%I', relation.schema_name, relation.table_name), 'TRIGGER') THEN
                RAISE EXCEPTION 'Runtime role % has prohibited privilege on %.%', runtime.role_name, relation.schema_name, relation.table_name;
            END IF;
        END LOOP;

        FOR column_row IN
            SELECT n.nspname AS schema_name, c.relname AS table_name, a.attname AS column_name
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE a.attnum > 0 AND NOT a.attisdropped AND c.relkind IN ('r', 'p')
              AND n.nspname IN ('public', 'v3')
        LOOP
            expected_value := COALESCE((SELECT t.can_select FROM _v3_expected_tables t
                WHERE t.role_kind = runtime.role_kind AND t.schema_name = column_row.schema_name
                  AND t.table_name = column_row.table_name), false);
            actual := has_column_privilege(runtime.role_name,
                format('%I.%I', column_row.schema_name, column_row.table_name),
                column_row.column_name, 'SELECT');
            IF actual <> expected_value THEN
                RAISE EXCEPTION 'Unexpected column SELECT privilege for % on %.%.%', runtime.role_name,
                    column_row.schema_name, column_row.table_name, column_row.column_name;
            END IF;

            expected_value := COALESCE((SELECT t.can_insert FROM _v3_expected_tables t
                WHERE t.role_kind = runtime.role_kind AND t.schema_name = column_row.schema_name
                  AND t.table_name = column_row.table_name), false);
            actual := has_column_privilege(runtime.role_name,
                format('%I.%I', column_row.schema_name, column_row.table_name),
                column_row.column_name, 'INSERT');
            IF actual <> expected_value THEN
                RAISE EXCEPTION 'Unexpected column INSERT privilege for % on %.%.%', runtime.role_name,
                    column_row.schema_name, column_row.table_name, column_row.column_name;
            END IF;

            expected_value := COALESCE((SELECT t.can_update FROM _v3_expected_tables t
                WHERE t.role_kind = runtime.role_kind AND t.schema_name = column_row.schema_name
                  AND t.table_name = column_row.table_name), false) OR EXISTS (
                SELECT 1 FROM _v3_expected_update_columns u
                WHERE u.role_kind = runtime.role_kind AND u.schema_name = column_row.schema_name
                  AND u.table_name = column_row.table_name AND u.column_name = column_row.column_name
            );
            actual := has_column_privilege(runtime.role_name,
                format('%I.%I', column_row.schema_name, column_row.table_name),
                column_row.column_name, 'UPDATE');
            IF actual <> expected_value THEN
                RAISE EXCEPTION 'Unexpected column UPDATE privilege for % on %.%.%', runtime.role_name,
                    column_row.schema_name, column_row.table_name, column_row.column_name;
            END IF;

            IF has_column_privilege(runtime.role_name,
                format('%I.%I', column_row.schema_name, column_row.table_name),
                column_row.column_name, 'REFERENCES') THEN
                RAISE EXCEPTION 'Unexpected column REFERENCES privilege for % on %.%.%', runtime.role_name,
                    column_row.schema_name, column_row.table_name, column_row.column_name;
            END IF;
        END LOOP;

        FOR routine IN
            SELECT p.oid, p.proname, pg_get_function_identity_arguments(p.oid) AS identity_arguments
            FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = 'v3' AND p.prokind IN ('f', 'p')
        LOOP
            expected_value := EXISTS (SELECT 1 FROM _v3_expected_functions
                WHERE role_kind = runtime.role_kind AND function_name = routine.proname
                  AND identity_arguments = routine.identity_arguments);
            actual := has_function_privilege(runtime.role_name, routine.oid, 'EXECUTE');
            IF actual <> expected_value THEN
                RAISE EXCEPTION 'Unexpected EXECUTE privilege for % on v3.%(%)', runtime.role_name,
                    routine.proname, routine.identity_arguments;
            END IF;
            IF EXISTS (
                SELECT 1 FROM aclexplode(COALESCE((SELECT proacl FROM pg_proc WHERE oid = routine.oid),
                    acldefault('f', (SELECT proowner FROM pg_proc WHERE oid = routine.oid)))) acl
                WHERE acl.grantee = 0 AND acl.privilege_type = 'EXECUTE'
            ) THEN
                RAISE EXCEPTION 'PUBLIC retains EXECUTE on v3.%(%)', routine.proname,
                    routine.identity_arguments;
            END IF;
        END LOOP;

        IF EXISTS (
            SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname IN ('public', 'v3') AND c.relkind = 'S'
              AND (has_sequence_privilege(runtime.role_name, c.oid, 'USAGE')
                OR has_sequence_privilege(runtime.role_name, c.oid, 'SELECT')
                OR has_sequence_privilege(runtime.role_name, c.oid, 'UPDATE'))
        ) THEN
            RAISE EXCEPTION 'Runtime role % has unexpected sequence privileges', runtime.role_name;
        END IF;

        IF EXISTS (
            SELECT 1 FROM pg_database d, LATERAL aclexplode(COALESCE(d.datacl, acldefault('d', d.datdba))) acl
            WHERE d.datname = current_database() AND acl.grantee = role_oid AND acl.is_grantable
            UNION ALL
            SELECT 1 FROM pg_namespace n, LATERAL aclexplode(COALESCE(n.nspacl, acldefault('n', n.nspowner))) acl
            WHERE acl.grantee = role_oid AND acl.is_grantable
            UNION ALL
            SELECT 1 FROM pg_class c, LATERAL aclexplode(COALESCE(c.relacl, acldefault(CASE WHEN c.relkind = 'S' THEN 'S'::"char" ELSE 'r'::"char" END, c.relowner))) acl
            WHERE acl.grantee = role_oid AND acl.is_grantable
            UNION ALL
            SELECT 1 FROM pg_attribute a, LATERAL aclexplode(a.attacl) acl
            WHERE a.attacl IS NOT NULL AND acl.grantee = role_oid AND acl.is_grantable
            UNION ALL
            SELECT 1 FROM pg_proc p, LATERAL aclexplode(COALESCE(p.proacl, acldefault('f', p.proowner))) acl
            WHERE acl.grantee = role_oid AND acl.is_grantable
        ) THEN
            RAISE EXCEPTION 'Runtime role % has a prohibited grant option', runtime.role_name;
        END IF;
    END LOOP;
END
$verify$;

ROLLBACK;
