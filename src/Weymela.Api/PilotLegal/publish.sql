\set ON_ERROR_STOP on

-- Phase 4A.1 Pilot/Draft legal publication only.
-- Run only under a separately authorized Pilot deployment using the protected
-- V3 migration identity. This script never changes acceptances or user data.
BEGIN;

SELECT pg_advisory_xact_lock(73434101);

DO $guard$
BEGIN
    IF current_database() <> 'weymela_v3_pilot' THEN
        RAISE EXCEPTION 'Pilot legal publication target rejected';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM v3."LegalDocumentVersions"
        WHERE "Type" = 'TermsOfService'
          AND "Version" = 'pilot-draft-2026-09-16.1'
          AND ("Id" <> 'a3878425-4127-41ba-8da5-81833ff8a184'::uuid
            OR "ContentHash" <> 'sha256:6b2ba370e35795c78624a7581addf4f8d4695c57013527f73c21abf764f0b5b7'
            OR "EffectiveFromUtc" <> '2026-09-16T00:00:00Z'::timestamptz)
    ) OR EXISTS (
        SELECT 1
        FROM v3."LegalDocumentVersions"
        WHERE "Type" = 'PrivacyPolicy'
          AND "Version" = 'pilot-draft-2026-09-16.1'
          AND ("Id" <> '6cc3f8a1-6e3e-40bf-add2-9d60dc1dd103'::uuid
            OR "ContentHash" <> 'sha256:7e4ece2fb2e348827112acbeec458c6169228cd214729675fc88d228fd645a12'
            OR "EffectiveFromUtc" <> '2026-09-16T00:00:00Z'::timestamptz)
    ) OR EXISTS (
        SELECT 1
        FROM v3."LegalDocumentVersions"
        WHERE "Id" = 'a3878425-4127-41ba-8da5-81833ff8a184'::uuid
          AND ("Type" <> 'TermsOfService' OR "Version" <> 'pilot-draft-2026-09-16.1')
    ) OR EXISTS (
        SELECT 1
        FROM v3."LegalDocumentVersions"
        WHERE "Id" = '6cc3f8a1-6e3e-40bf-add2-9d60dc1dd103'::uuid
          AND ("Type" <> 'PrivacyPolicy' OR "Version" <> 'pilot-draft-2026-09-16.1')
    ) THEN
        RAISE EXCEPTION 'Pilot legal document version conflict';
    END IF;
END
$guard$;

INSERT INTO v3."LegalDocumentVersions"
    ("Id", "Type", "Version", "ContentHash", "EffectiveFromUtc")
VALUES
    ('a3878425-4127-41ba-8da5-81833ff8a184', 'TermsOfService',
     'pilot-draft-2026-09-16.1',
     'sha256:6b2ba370e35795c78624a7581addf4f8d4695c57013527f73c21abf764f0b5b7',
     '2026-09-16T00:00:00Z'),
    ('6cc3f8a1-6e3e-40bf-add2-9d60dc1dd103', 'PrivacyPolicy',
     'pilot-draft-2026-09-16.1',
     'sha256:7e4ece2fb2e348827112acbeec458c6169228cd214729675fc88d228fd645a12',
     '2026-09-16T00:00:00Z')
ON CONFLICT ("Type", "Version") DO NOTHING;

DO $verify$
BEGIN
    IF (SELECT count(*) FROM v3."LegalDocumentVersions"
        WHERE ("Id", "Type", "Version", "ContentHash", "EffectiveFromUtc") IN (
            ('a3878425-4127-41ba-8da5-81833ff8a184'::uuid, 'TermsOfService',
             'pilot-draft-2026-09-16.1',
             'sha256:6b2ba370e35795c78624a7581addf4f8d4695c57013527f73c21abf764f0b5b7',
             '2026-09-16T00:00:00Z'::timestamptz),
            ('6cc3f8a1-6e3e-40bf-add2-9d60dc1dd103'::uuid, 'PrivacyPolicy',
             'pilot-draft-2026-09-16.1',
             'sha256:7e4ece2fb2e348827112acbeec458c6169228cd214729675fc88d228fd645a12',
             '2026-09-16T00:00:00Z'::timestamptz)
        )) <> 2 THEN
        RAISE EXCEPTION 'Pilot legal publication verification failed';
    END IF;
END
$verify$;

COMMIT;
