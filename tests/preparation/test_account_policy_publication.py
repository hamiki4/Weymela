import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import uuid

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('publication', ROOT / 'tools/ops/prepare-account-policy-publication.py')
PUBLICATION = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PUBLICATION)


class AccountPolicyPublicationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        documents = []
        for kind in ['TermsOfService', 'PrivacyPolicy']:
            for language in ['en', 'am']:
                content = f'Isolated test policy; {kind}; {language}.\n'.encode()
                (self.directory / f'{kind}-{language}.txt').write_bytes(content)
                documents.append({'type': kind, 'language': language, 'approvedFinalVersion': f'isolated-1-{language}',
                                  'approvedFinalContentSha256': 'sha256:' + hashlib.sha256(content).hexdigest(),
                                  'effectiveFromUtc': '2026-01-01T00:00:00Z'})
        self.manifest = {'environment': 'Production', 'productionApproved': True, 'attorneyReviewed': True,
                         'ownerApprovalReference': "isolated owner ' reference", 'legalApprovalReference': 'isolated legal reference',
                         'documents': documents}
        self.operator = str(uuid.uuid4())

    def prepare(self):
        (self.directory / 'publication.json').write_text(json.dumps(self.manifest))
        return PUBLICATION.prepare(self.directory, self.operator)

    def test_prepares_stable_append_only_publication_without_acceptance_or_runtime_changes(self):
        first = self.prepare()
        self.assertEqual(first, self.prepare())
        self.assertIn("current_database() <> 'weymela_v3_prod_main'", first)
        self.assertIn("AND \"Role\" = 'PlatformAdmin' AND \"IsActive\"", first)
        self.assertIn("owner '' reference", first)
        self.assertIn('Published policy versions are immutable', first)
        self.assertIn('AccountLegalDocumentsPublished', first)
        self.assertNotIn('LegalAcceptances', first)
        self.assertNotIn('UPDATE ', first)
        self.assertNotIn('DELETE ', first)
        self.assertNotIn('Financial', first)

    def test_rejects_unapproved_incomplete_or_changed_content(self):
        for field in ['productionApproved', 'attorneyReviewed', 'ownerApprovalReference', 'legalApprovalReference']:
            original = self.manifest[field]
            self.manifest[field] = False if isinstance(original, bool) else ''
            with self.assertRaises(ValueError):
                self.prepare()
            self.manifest[field] = original
        self.manifest['documents'].pop()
        with self.assertRaises(ValueError):
            self.prepare()

    def test_rejects_unapproved_bytes_even_when_approval_metadata_is_present(self):
        (self.directory / 'PrivacyPolicy-am.txt').write_text('changed content')
        with self.assertRaisesRegex(ValueError, 'exact UTF-8 bytes'):
            self.prepare()

    def test_rejects_placeholders_and_future_or_ambiguous_effective_dates(self):
        self.manifest['documents'][0]['effectiveFromUtc'] = '2099-01-01T00:00:00Z'
        with self.assertRaises(ValueError):
            self.prepare()
        self.manifest['documents'][0]['effectiveFromUtc'] = '2026-01-01T00:00:00'
        with self.assertRaises(ValueError):
            self.prepare()
        (self.directory / 'TermsOfService-en.txt').write_text('[OWNER/COUNSEL: approval missing]')
        with self.assertRaisesRegex(ValueError, 'placeholders'):
            self.prepare()
