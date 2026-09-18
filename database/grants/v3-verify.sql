\set ON_ERROR_STOP on

-- Required psql variables: api_role, worker_role, migrator_role, backup_role,
-- database_name. Run as the database owner after all four grant scripts.
-- This verifier compares V3 privileges and rejects missing or extra access.

BEGIN;

CREATE TEMP TABLE _v3_runtime_roles (
    role_name name PRIMARY KEY,
    role_kind text NOT NULL CHECK (role_kind IN ('api', 'worker'))
) ON COMMIT DROP;
INSERT INTO _v3_runtime_roles VALUES
    (:'api_role', 'api'),
    (:'worker_role', 'worker');

CREATE TEMP TABLE _v3_operational_roles (
    migrator_name name NOT NULL,
    backup_name name NOT NULL
) ON COMMIT DROP;
INSERT INTO _v3_operational_roles VALUES (:'migrator_role', :'backup_role');

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
    ('api', 'v3', 'PasswordCredentials', true, true, true),
    ('api', 'v3', 'IdentityBindings', true, true, false),
    ('api', 'v3', 'CommercePermissions', true, true, true),
    ('api', 'v3', 'PublicWorkspaceProfiles', true, true, false),
    ('api', 'v3', 'RoleEnrollments', true, true, true),
    ('api', 'v3', 'BusinessWallets', true, true, true),
    ('api', 'v3', 'CustomerCashbackAccounts', true, true, true),
    ('api', 'v3', 'CustomerProfiles', true, true, false),
    ('api', 'v3', 'LegalDocumentVersions', true, false, false),
    ('api', 'v3', 'LegalAcceptances', true, true, false),
    ('api', 'v3', 'AuthorizedDevices', true, true, true),
    ('api', 'v3', 'DeviceSessions', true, true, true),
    ('api', 'v3', 'ProductHandoffTransactions', true, true, true),
    ('api', 'v3', 'IdempotencyRecords', true, true, false),
    ('api', 'v3', 'AuditEvents', true, true, false),
    ('api', 'v3', 'OutboxMessages', true, true, false),
    ('api', 'v3', 'InAppNotifications', true, true, false),
    ('api', 'v3', 'OfferQrSessions', true, true, true),
    ('api', 'v3', 'WorkerCheckpoints', true, false, false),
    ('api', 'v3', 'AdminGrants', true, true, true),
    ('api', 'v3', 'CreatorApplications', true, true, true),
    ('api', 'v3', 'CreatorAllocations', true, true, true),
    ('api', 'v3', 'CreatorEarningsAccounts', true, true, true),
    ('api', 'v3', 'CreatorPromotionParticipations', true, true, true),
    ('api', 'v3', 'CreatorSocialProfiles', true, true, true),
    ('api', 'v3', 'DepositRequests', true, true, true),
    ('api', 'v3', 'PayoutRecords', true, true, true),
    ('api', 'v3', 'PromotionPlatforms', true, true, true),
    ('api', 'v3', 'Promotions', true, true, true),
    ('api', 'v3', 'UgcAssignments', true, true, true),
    ('api', 'v3', 'UgcCustomerOffers', true, true, true),
    ('api', 'v3', 'UgcCreatorRequests', true, true, true),
    ('api', 'v3', 'UgcOpportunities', true, true, true),
    ('api', 'v3', 'UgcSubmissions', true, true, true),
    ('api', 'v3', 'CreatorEarningEntries', true, true, false),
    ('api', 'v3', 'CustomerCashbackEntries', true, true, false),
    ('api', 'v3', 'FinancialConfigurationVersions', true, true, false),
    ('api', 'v3', 'FinancialJournals', true, true, false),
    ('api', 'v3', 'FinancialJournalLines', true, true, false),
    ('api', 'v3', 'PlatformRevenueEntries', true, true, false),
    ('api', 'v3', 'PlatformSettlements', true, true, false),
    ('api', 'v3', 'PricingSnapshots', true, true, false),
    ('api', 'v3', 'PromotionBudgetEntries', true, true, false),
    ('api', 'v3', 'PromotionReservations', true, true, false),
    ('api', 'v3', 'VerifiedSales', true, true, false),
    ('api', 'v3', 'ViewRewardReceipts', true, true, false),
    ('api', 'v3', 'WalletEntries', true, true, false),
    ('api', 'v3', 'UgcBudgetEntries', true, true, false),
    ('api', 'v3', 'UgcCustomerOfferBudgetEntries', true, true, false),
    ('api', 'v3', 'UgcCustomerOfferReservations', true, true, false),
    ('api', 'v3', 'UgcCustomerOfferSales', true, true, false),
    ('api', 'v3', 'UgcPlatformRequirements', true, true, false),
    ('api', 'v3', 'UgcReservations', true, true, false),
    ('api', 'v3', 'UgcRevisions', true, true, false),
    ('worker', 'v3', 'WorkerCheckpoints', true, true, true),
    ('worker', 'v3', 'FinancialConfigurationVersions', true, false, false),
    ('worker', 'v3', 'OutboxMessages', true, true, true),
    ('worker', 'v3', 'CommercePermissions', true, false, false),
    ('worker', 'v3', 'InAppNotifications', true, true, false),
    ('worker', 'v3', 'OfferQrSessions', true, false, false),
    ('worker', 'v3', 'Promotions', true, false, false),
    ('worker', 'v3', 'PricingSnapshots', true, false, false),
    ('worker', 'v3', 'CreatorAllocations', true, false, false),
    ('worker', 'v3', 'RoleEnrollments', true, false, false),
    ('worker', 'v3', 'UgcAssignments', true, false, false),
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
    IF EXISTS (
        SELECT 1 FROM pg_database d,
            LATERAL aclexplode(COALESCE(d.datacl, acldefault('d', d.datdba))) acl
        WHERE d.datname = current_database() AND acl.grantee = 0
            AND acl.privilege_type IN ('CONNECT', 'CREATE', 'TEMPORARY')
        UNION ALL
        SELECT 1 FROM pg_namespace n,
            LATERAL aclexplode(COALESCE(n.nspacl, acldefault('n', n.nspowner))) acl
        WHERE n.nspname IN ('public', 'v3') AND acl.grantee = 0
            AND acl.privilege_type IN ('USAGE', 'CREATE')
        UNION ALL
        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace,
            LATERAL aclexplode(COALESCE(c.relacl,
                acldefault(CASE WHEN c.relkind = 'S' THEN 'S'::"char"
                    ELSE 'r'::"char" END, c.relowner))) acl
        WHERE n.nspname IN ('public', 'v3') AND c.relkind IN ('r', 'p', 'S')
            AND acl.grantee = 0
        UNION ALL
        SELECT 1 FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace,
            LATERAL aclexplode(a.attacl) acl
        WHERE n.nspname IN ('public', 'v3') AND a.attacl IS NOT NULL
            AND acl.grantee = 0
    ) THEN
        RAISE EXCEPTION 'PUBLIC retains database, schema or relation privileges';
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

DO $operations$
DECLARE
    migrator_name name := (SELECT migrator_name FROM _v3_operational_roles);
    backup_name name := (SELECT backup_name FROM _v3_operational_roles);
    migrator_oid oid;
    backup_oid oid;
    relation record;
    column_row record;
    routine record;
    default_row record;
BEGIN
    SELECT oid INTO migrator_oid FROM pg_roles WHERE rolname = migrator_name;
    SELECT oid INTO backup_oid FROM pg_roles WHERE rolname = backup_name;
    IF migrator_oid IS NULL OR backup_oid IS NULL OR migrator_oid = backup_oid
        OR migrator_name IN (SELECT role_name FROM _v3_runtime_roles)
        OR backup_name IN (SELECT role_name FROM _v3_runtime_roles) THEN
        RAISE EXCEPTION 'Operational roles are missing, duplicate or runtime roles';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE oid IN (migrator_oid, backup_oid)
        AND (rolsuper OR rolcreaterole OR rolcreatedb OR rolreplication OR rolbypassrls))
        OR EXISTS (SELECT 1 FROM pg_auth_members
            WHERE member IN (migrator_oid, backup_oid)
               OR roleid IN (migrator_oid, backup_oid))
        OR EXISTS (SELECT 1 FROM pg_database WHERE datdba IN (migrator_oid, backup_oid)) THEN
        RAISE EXCEPTION 'Operational role has prohibited attributes, membership or database ownership';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_namespace WHERE nspowner = backup_oid)
        OR EXISTS (SELECT 1 FROM pg_class WHERE relowner = backup_oid)
        OR EXISTS (SELECT 1 FROM pg_proc WHERE proowner = backup_oid)
        OR EXISTS (SELECT 1 FROM pg_default_acl WHERE defaclrole = backup_oid) THEN
        RAISE EXCEPTION 'Backup role owns objects or default privileges';
    END IF;
    IF (SELECT nspowner FROM pg_namespace WHERE nspname = 'v3') IS DISTINCT FROM migrator_oid
        OR (SELECT relowner FROM pg_class WHERE oid = to_regclass('public."__EFMigrationsHistory"'))
            IS DISTINCT FROM migrator_oid
        OR EXISTS (SELECT 1 FROM pg_namespace WHERE nspowner = migrator_oid AND nspname <> 'v3')
        OR EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relowner = migrator_oid AND NOT (n.nspname = 'v3'
                OR n.nspname LIKE 'pg_toast%'
                OR c.oid = to_regclass('public."__EFMigrationsHistory"')
                OR c.oid IN (SELECT indexrelid FROM pg_index
                    WHERE indrelid = to_regclass('public."__EFMigrationsHistory"'))))
        OR EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'v3' AND c.relowner <> migrator_oid)
        OR EXISTS (SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = 'v3' AND p.proowner <> migrator_oid) THEN
        RAISE EXCEPTION 'Migration ownership is missing or extends beyond reviewed V3 objects';
    END IF;

    IF NOT has_database_privilege(migrator_name, current_database(), 'CONNECT')
        OR NOT has_database_privilege(backup_name, current_database(), 'CONNECT')
        OR has_database_privilege(migrator_name, current_database(), 'CREATE')
        OR has_database_privilege(backup_name, current_database(), 'CREATE')
        OR has_database_privilege(migrator_name, current_database(), 'TEMPORARY')
        OR has_database_privilege(backup_name, current_database(), 'TEMPORARY') THEN
        RAISE EXCEPTION 'Operational database privileges differ from CONNECT-only contract';
    END IF;
    IF NOT has_schema_privilege(migrator_name, 'public', 'USAGE')
        OR NOT has_schema_privilege(migrator_name, 'public', 'CREATE')
        OR NOT has_schema_privilege(migrator_name, 'v3', 'USAGE')
        OR NOT has_schema_privilege(migrator_name, 'v3', 'CREATE')
        OR NOT has_schema_privilege(backup_name, 'public', 'USAGE')
        OR NOT has_schema_privilege(backup_name, 'v3', 'USAGE')
        OR has_schema_privilege(backup_name, 'public', 'CREATE')
        OR has_schema_privilege(backup_name, 'v3', 'CREATE') THEN
        RAISE EXCEPTION 'Operational schema privileges differ from migration/read-only contract';
    END IF;
    IF EXISTS (
        SELECT 1 FROM pg_database d, LATERAL aclexplode(COALESCE(d.datacl,
            acldefault('d', d.datdba))) acl
        WHERE d.datname = current_database() AND acl.grantee IN (migrator_oid, backup_oid)
            AND (acl.privilege_type <> 'CONNECT' OR acl.is_grantable)
        UNION ALL
        SELECT 1 FROM pg_namespace n, LATERAL aclexplode(COALESCE(n.nspacl,
            acldefault('n', n.nspowner))) acl
        WHERE acl.grantee IN (migrator_oid, backup_oid)
            AND (n.nspname NOT IN ('public', 'v3')
                OR (acl.grantee = backup_oid AND acl.privilege_type <> 'USAGE')
                OR (acl.grantee = migrator_oid AND acl.privilege_type NOT IN ('USAGE', 'CREATE'))
                OR (acl.is_grantable AND NOT
                    (acl.grantee = migrator_oid AND n.nspowner = migrator_oid)))
        UNION ALL
        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace,
            LATERAL aclexplode(COALESCE(c.relacl,
                acldefault(CASE WHEN c.relkind = 'S' THEN 'S'::"char"
                    ELSE 'r'::"char" END, c.relowner))) acl
        WHERE acl.grantee IN (migrator_oid, backup_oid)
            AND (NOT (acl.grantee = migrator_oid AND c.relowner = migrator_oid
                    AND (n.nspname = 'v3' OR n.nspname LIKE 'pg_toast%'
                        OR c.oid = to_regclass('public."__EFMigrationsHistory"')
                        OR c.oid IN (SELECT indexrelid FROM pg_index
                            WHERE indrelid = to_regclass('public."__EFMigrationsHistory"'))))
                AND (n.nspname NOT IN ('public', 'v3')
                    OR (acl.grantee = backup_oid AND acl.privilege_type <> 'SELECT')
                    OR acl.is_grantable))
        UNION ALL
        SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace,
            LATERAL aclexplode(COALESCE(p.proacl, acldefault('f', p.proowner))) acl
        WHERE acl.grantee = backup_oid AND acl.privilege_type = 'EXECUTE'
    ) THEN
        RAISE EXCEPTION 'Operational direct privileges exceed reviewed contract';
    END IF;

    FOR relation IN
        SELECT n.nspname AS schema_name, c.relname AS table_name, c.oid
        FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind IN ('r', 'p') AND n.nspname IN ('public', 'v3')
    LOOP
        IF NOT has_table_privilege(backup_name, relation.oid, 'SELECT')
            OR has_table_privilege(backup_name, relation.oid, 'INSERT')
            OR has_table_privilege(backup_name, relation.oid, 'UPDATE')
            OR has_table_privilege(backup_name, relation.oid, 'DELETE')
            OR has_table_privilege(backup_name, relation.oid, 'TRUNCATE')
            OR has_table_privilege(backup_name, relation.oid, 'REFERENCES')
            OR has_table_privilege(backup_name, relation.oid, 'TRIGGER') THEN
            RAISE EXCEPTION 'Backup table privilege differs from SELECT-only on %.%',
                relation.schema_name, relation.table_name;
        END IF;
    END LOOP;
    FOR column_row IN
        SELECT n.nspname AS schema_name, c.relname AS table_name, a.attname AS column_name, c.oid
        FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE a.attnum > 0 AND NOT a.attisdropped AND c.relkind IN ('r', 'p')
            AND n.nspname IN ('public', 'v3')
    LOOP
        IF NOT has_column_privilege(backup_name, column_row.oid, column_row.column_name, 'SELECT')
            OR has_column_privilege(backup_name, column_row.oid, column_row.column_name, 'INSERT')
            OR has_column_privilege(backup_name, column_row.oid, column_row.column_name, 'UPDATE')
            OR has_column_privilege(backup_name, column_row.oid, column_row.column_name, 'REFERENCES') THEN
            RAISE EXCEPTION 'Backup column privilege differs from SELECT-only on %.%.%',
                column_row.schema_name, column_row.table_name, column_row.column_name;
        END IF;
    END LOOP;
    FOR relation IN
        SELECT c.oid, n.nspname AS schema_name, c.relname AS sequence_name
        FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind = 'S' AND n.nspname IN ('public', 'v3')
    LOOP
        IF NOT has_sequence_privilege(backup_name, relation.oid, 'SELECT')
            OR has_sequence_privilege(backup_name, relation.oid, 'USAGE')
            OR has_sequence_privilege(backup_name, relation.oid, 'UPDATE') THEN
            RAISE EXCEPTION 'Backup sequence privilege differs from SELECT-only on %.%',
                relation.schema_name, relation.sequence_name;
        END IF;
    END LOOP;
    FOR routine IN
        SELECT p.oid, p.proname FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = 'v3' AND p.prokind IN ('f', 'p')
    LOOP
        IF has_function_privilege(backup_name, routine.oid, 'EXECUTE') THEN
            RAISE EXCEPTION 'Backup may not execute V3 routine %', routine.proname;
        END IF;
    END LOOP;

    FOR default_row IN
        SELECT n.oid AS namespace_oid, n.nspname AS schema_name, kind.objtype
        FROM pg_namespace n CROSS JOIN (VALUES ('r'::"char"), ('S'::"char")) kind(objtype)
        WHERE n.nspname IN ('public', 'v3')
    LOOP
        IF NOT EXISTS (
            SELECT 1 FROM pg_default_acl d, LATERAL aclexplode(d.defaclacl) acl
            WHERE d.defaclrole = migrator_oid AND d.defaclnamespace = default_row.namespace_oid
                AND d.defaclobjtype = default_row.objtype
                AND acl.grantee = backup_oid AND acl.privilege_type = 'SELECT'
                AND NOT acl.is_grantable
        ) THEN
            RAISE EXCEPTION 'Backup future-object SELECT default missing for %', default_row.schema_name;
        END IF;
    END LOOP;
    IF NOT EXISTS (SELECT 1 FROM pg_default_acl d WHERE d.defaclrole = migrator_oid
        AND d.defaclnamespace = 0 AND d.defaclobjtype = 'f')
        OR EXISTS (SELECT 1 FROM pg_default_acl d, LATERAL aclexplode(d.defaclacl) acl
            WHERE d.defaclrole = migrator_oid AND d.defaclobjtype = 'f'
                AND acl.grantee = 0 AND acl.privilege_type = 'EXECUTE')
        OR EXISTS (SELECT 1 FROM pg_default_acl d, LATERAL aclexplode(d.defaclacl) acl
            WHERE d.defaclrole = migrator_oid AND d.defaclobjtype IN ('r', 'S')
                AND d.defaclnamespace IN
                    (SELECT oid FROM pg_namespace WHERE nspname IN ('public', 'v3'))
                AND acl.grantee = 0)
        OR EXISTS (SELECT 1 FROM pg_default_acl d, LATERAL aclexplode(d.defaclacl) acl
            WHERE d.defaclrole = migrator_oid AND acl.grantee IN
                (backup_oid, (SELECT oid FROM pg_roles WHERE rolname = (SELECT role_name
                    FROM _v3_runtime_roles WHERE role_kind = 'api')),
                 (SELECT oid FROM pg_roles WHERE rolname = (SELECT role_name
                    FROM _v3_runtime_roles WHERE role_kind = 'worker')))
                AND NOT (acl.grantee = backup_oid AND d.defaclnamespace IN
                    (SELECT oid FROM pg_namespace WHERE nspname IN ('public', 'v3'))
                    AND d.defaclobjtype IN ('r', 'S') AND acl.privilege_type = 'SELECT'
                    AND NOT acl.is_grantable)) THEN
        RAISE EXCEPTION 'Migration-owner default privileges exceed reviewed backup/future-function contract';
    END IF;
    IF EXISTS (
        SELECT 1 FROM pg_database d, LATERAL aclexplode(COALESCE(d.datacl, acldefault('d', d.datdba))) acl
        WHERE d.datname = current_database() AND acl.grantee = backup_oid AND acl.is_grantable
        UNION ALL
        SELECT 1 FROM pg_namespace n, LATERAL aclexplode(COALESCE(n.nspacl, acldefault('n', n.nspowner))) acl
        WHERE acl.grantee = backup_oid AND acl.is_grantable
        UNION ALL
        SELECT 1 FROM pg_class c, LATERAL aclexplode(COALESCE(c.relacl, acldefault('r', c.relowner))) acl
        WHERE acl.grantee = backup_oid AND acl.is_grantable
        UNION ALL
        SELECT 1 FROM pg_attribute a, LATERAL aclexplode(a.attacl) acl
        WHERE a.attacl IS NOT NULL AND acl.grantee = backup_oid AND acl.is_grantable
        UNION ALL
        SELECT 1 FROM pg_proc p, LATERAL aclexplode(COALESCE(p.proacl, acldefault('f', p.proowner))) acl
        WHERE acl.grantee = backup_oid AND acl.is_grantable
    ) THEN
        RAISE EXCEPTION 'Backup role has a prohibited grant option';
    END IF;
END
$operations$;

ROLLBACK;
