#!/usr/bin/env python3
"""Validate a genuinely approved Production bundle and prepare append-only SQL.

This tool never approves documents, changes runtime flags, records consent or connects
to a database. Execute the reviewed SQL with the protected Production operator role,
then mount the same approved bundle read-only before enabling Required consent.
"""
import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import uuid


def quote(value):
    return "'" + str(value).replace("'", "''") + "'"


def prepare(directory, operator):
    operator = str(uuid.UUID(operator))
    manifest = json.loads((directory / 'publication.json').read_text(encoding='utf-8'))
    if (manifest.get('environment') != 'Production'
            or manifest.get('productionApproved') is not True
            or manifest.get('attorneyReviewed') is not True
            or not all(isinstance(manifest.get(k), str) and manifest[k].strip()
                       for k in ['ownerApprovalReference', 'legalApprovalReference'])):
        raise ValueError('Actual owner and legal approvals are required; drafts cannot be published.')
    expected = {(kind, language) for kind in ['TermsOfService', 'PrivacyPolicy'] for language in ['en', 'am']}
    documents = manifest.get('documents', [])
    if len(documents) != 4 or {(d.get('type'), d.get('language')) for d in documents} != expected:
        raise ValueError('Exactly four approved English/Amharic Terms and Privacy documents are required.')
    rows = []
    for document in documents:
        kind, language = document['type'], document['language']
        content = (directory / f'{kind}-{language}.txt').read_bytes()
        if not 0 < len(content) <= 262144:
            raise ValueError('Policy content is empty or exceeds the supported size.')
        text = content.decode('utf-8', errors='strict')
        if any(marker in text.upper() for marker in ['[OWNER', '[COUNSEL', '[TODO', '[TBD', 'OWNER/COUNSEL', 'OWNER-LEGAL-DRAFT']):
            raise ValueError('Unresolved policy placeholders cannot be published.')
        digest = 'sha256:' + hashlib.sha256(content).hexdigest()
        version = document['approvedFinalVersion']
        if not isinstance(version, str) or not version.strip() or not version.endswith('-' + language):
            raise ValueError('An approved final version with its language suffix is required.')
        if document.get('approvedFinalContentSha256') != digest:
            raise ValueError('The exact UTF-8 bytes differ from the approved content hash.')
        effective = dt.datetime.fromisoformat(document['effectiveFromUtc'].replace('Z', '+00:00'))
        if effective.tzinfo is None or effective > dt.datetime.now(dt.timezone.utc):
            raise ValueError('Current publication requires an approved, effective UTC timestamp.')
        identifier = str(uuid.uuid5(uuid.NAMESPACE_URL, f'https://weymela.com/legal/{kind}/{version}'))
        rows.append((identifier, kind, version, digest, effective.isoformat()))
    rows.sort()
    values = ',\n'.join('(' + ', '.join(quote(v) for v in row) + ')' for row in rows)
    audit_id = str(uuid.uuid5(uuid.NAMESPACE_URL, json.dumps(rows)))
    detail = json.dumps({'ownerApprovalReference': manifest['ownerApprovalReference'],
                        'legalApprovalReference': manifest['legalApprovalReference'],
                        'documents': [r[0] for r in rows]}, separators=(',', ':'))
    return f'''-- Prepared from exact approved bytes. Review before applying with the protected operator role.
BEGIN;
SET LOCAL lock_timeout = '5s';
SELECT pg_advisory_xact_lock(hashtextextended('v3-account-policy-publication', 0));
CREATE TEMP TABLE approved_account_policies (id uuid, type text, version text, hash text, effective timestamptz) ON COMMIT DROP;
INSERT INTO approved_account_policies VALUES
{values};
DO $publication$
BEGIN
    IF current_database() <> 'weymela_v3_prod_main' THEN RAISE EXCEPTION 'Production database required'; END IF;
    IF NOT EXISTS (SELECT 1 FROM v3."CommercePermissions" WHERE "UserId" = {quote(operator)}::uuid
        AND "Role" = 'PlatformAdmin' AND "IsActive") THEN RAISE EXCEPTION 'Active Platform Admin operator required'; END IF;
    IF EXISTS (SELECT 1 FROM approved_account_policies a JOIN v3."LegalDocumentVersions" p
        ON p."Id" = a.id OR (p."Type" = a.type AND p."Version" = a.version)
        WHERE p."Id" <> a.id OR p."Type" <> a.type OR p."Version" <> a.version
            OR p."ContentHash" <> a.hash OR p."EffectiveFromUtc" <> a.effective)
    THEN RAISE EXCEPTION 'Published policy versions are immutable'; END IF;
    IF EXISTS (SELECT 1 FROM approved_account_policies a JOIN v3."LegalDocumentVersions" p
        ON p."Type" = a.type AND right(p."Version", 3) = right(a.version, 3)
        WHERE p."EffectiveFromUtc" >= a.effective AND p."EffectiveFromUtc" <= now() AND p."Version" <> a.version)
    THEN RAISE EXCEPTION 'A newer policy is already effective'; END IF;
END $publication$;
INSERT INTO v3."LegalDocumentVersions" ("Id", "Type", "Version", "ContentHash", "EffectiveFromUtc")
SELECT id, type, version, hash, effective FROM approved_account_policies ON CONFLICT ("Id") DO NOTHING;
INSERT INTO v3."AuditEvents" ("Id", "EventType", "ActorId", "BusinessId", "PromotionId", "CreatorId", "CorrelationId", "OccurredAtUtc", "Detail")
VALUES ({quote(audit_id)}::uuid, 'AccountLegalDocumentsPublished', {quote(operator)}::uuid,
    NULL, NULL, NULL, {quote(audit_id)}::uuid, now(), {quote(detail)}) ON CONFLICT ("Id") DO NOTHING;
COMMIT;
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--approved-bundle', type=Path, required=True)
    parser.add_argument('--operator-user-id', required=True)
    parser.add_argument('--output-sql', type=Path, required=True)
    args = parser.parse_args()
    sql = prepare(args.approved_bundle, args.operator_user_id)
    with args.output_sql.open('x', encoding='utf-8') as output:
        output.write(sql)
    print('Four approved policies validated; append-only publication SQL prepared. Nothing was published or accepted.')


if __name__ == '__main__':
    main()
