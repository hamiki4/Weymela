\set ON_ERROR_STOP on

-- Explicit legal-version publication only. The caller must set
-- weymela.business_legal_publication_target to the exact target database
-- on this connection before running this script. No acceptance is created.
BEGIN;

SELECT pg_advisory_xact_lock(73434102);

DO $guard$
BEGIN
    IF current_setting('weymela.business_legal_publication_target', true)
        IS DISTINCT FROM current_database() THEN
        RAISE EXCEPTION 'Business legal publication target rejected';
    END IF;

    IF EXISTS (
        SELECT 1 FROM v3."LegalDocumentVersions"
        WHERE "Type" = 'BusinessAgreement' AND "Version" = 'initial-2026-09-28.1'
          AND ("Id" <> 'a592e8f7-66a7-458a-8b6b-37b15c5926af'::uuid
            OR "ContentHash" <> 'sha256:b2f974660a0b69174d0de01214a78009326d84fe43d2c9407222eae7506aa690'
            OR "EffectiveFromUtc" <> '2026-09-28T00:00:00Z'::timestamptz)
    ) OR EXISTS (
        SELECT 1 FROM v3."LegalDocumentVersions"
        WHERE "Type" = 'AntiCircumventionAgreement' AND "Version" = 'initial-2026-09-28.1'
          AND ("Id" <> '59b49723-1c3a-4b8f-a51d-d1f2f959d1de'::uuid
            OR "ContentHash" <> 'sha256:05a297468a6a513bc604a21c0e58a39d06c60ae8bdee8d3462a968d619f4078e'
            OR "EffectiveFromUtc" <> '2026-09-28T00:00:00Z'::timestamptz)
    ) OR EXISTS (
        SELECT 1 FROM v3."LegalDocumentVersions"
        WHERE "Id" = 'a592e8f7-66a7-458a-8b6b-37b15c5926af'::uuid
          AND ("Type" <> 'BusinessAgreement' OR "Version" <> 'initial-2026-09-28.1')
    ) OR EXISTS (
        SELECT 1 FROM v3."LegalDocumentVersions"
        WHERE "Id" = '59b49723-1c3a-4b8f-a51d-d1f2f959d1de'::uuid
          AND ("Type" <> 'AntiCircumventionAgreement' OR "Version" <> 'initial-2026-09-28.1')
    ) THEN
        RAISE EXCEPTION 'Business legal document version conflict';
    END IF;
END
$guard$;

INSERT INTO v3."LegalDocumentVersions"
    ("Id", "Type", "Version", "ContentHash", "EffectiveFromUtc")
VALUES
    ('a592e8f7-66a7-458a-8b6b-37b15c5926af', 'BusinessAgreement',
     'initial-2026-09-28.1',
     'sha256:b2f974660a0b69174d0de01214a78009326d84fe43d2c9407222eae7506aa690',
     '2026-09-28T00:00:00Z'),
    ('59b49723-1c3a-4b8f-a51d-d1f2f959d1de', 'AntiCircumventionAgreement',
     'initial-2026-09-28.1',
     'sha256:05a297468a6a513bc604a21c0e58a39d06c60ae8bdee8d3462a968d619f4078e',
     '2026-09-28T00:00:00Z')
ON CONFLICT ("Type", "Version") DO NOTHING;

DO $verify$
BEGIN
    IF (SELECT count(*) FROM v3."LegalDocumentVersions"
        WHERE ("Id", "Type", "Version", "ContentHash", "EffectiveFromUtc") IN (
            ('a592e8f7-66a7-458a-8b6b-37b15c5926af'::uuid, 'BusinessAgreement',
             'initial-2026-09-28.1',
             'sha256:b2f974660a0b69174d0de01214a78009326d84fe43d2c9407222eae7506aa690',
             '2026-09-28T00:00:00Z'::timestamptz),
            ('59b49723-1c3a-4b8f-a51d-d1f2f959d1de'::uuid, 'AntiCircumventionAgreement',
             'initial-2026-09-28.1',
             'sha256:05a297468a6a513bc604a21c0e58a39d06c60ae8bdee8d3462a968d619f4078e',
             '2026-09-28T00:00:00Z'::timestamptz)
        )) <> 2 OR
        (SELECT "Id" FROM v3."LegalDocumentVersions"
         WHERE "Type" = 'BusinessAgreement' AND "EffectiveFromUtc" <= clock_timestamp()
         ORDER BY "EffectiveFromUtc" DESC, "Version" DESC, "Id" DESC LIMIT 1)
             IS DISTINCT FROM 'a592e8f7-66a7-458a-8b6b-37b15c5926af'::uuid OR
        (SELECT "Id" FROM v3."LegalDocumentVersions"
         WHERE "Type" = 'AntiCircumventionAgreement' AND "EffectiveFromUtc" <= clock_timestamp()
         ORDER BY "EffectiveFromUtc" DESC, "Version" DESC, "Id" DESC LIMIT 1)
             IS DISTINCT FROM '59b49723-1c3a-4b8f-a51d-d1f2f959d1de'::uuid THEN
        RAISE EXCEPTION 'Business legal publication verification failed';
    END IF;
END
$verify$;

COMMIT;
