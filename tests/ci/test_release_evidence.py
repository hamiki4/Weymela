"""Private GHCR policy/evidence tests. Synthetic artifacts only; no image builds."""
import copy
import importlib.util
import json
import os
import pathlib
import subprocess
import sys
import tempfile
import textwrap
import unittest
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[2]
RELEASE = (ROOT/'.github/workflows/release.yml').read_text()
spec = importlib.util.spec_from_file_location('release_evidence', ROOT/'tools/ci/release-evidence.py')
evidence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evidence)


class AttestationPolicyTests(unittest.TestCase):
    def test_private_user_repo_is_unavailable_even_with_opt_in(self):
        for enabled in ('', 'false', 'true'):
            self.assertEqual(evidence.attestation_mode('true', 'User', enabled), 'unavailable')

    def test_public_repository_requires_explicit_opt_in(self):
        self.assertEqual(evidence.attestation_mode('false', 'User', ''), 'not-enabled')
        self.assertEqual(evidence.attestation_mode('false', 'User', 'true'), 'enabled')

    def test_private_organization_requires_owner_confirmation_of_support(self):
        self.assertEqual(evidence.attestation_mode('true', 'Organization', ''), 'not-enabled')
        self.assertEqual(evidence.attestation_mode('true', 'Organization', 'true'), 'enabled')

    def test_missing_repository_metadata_is_not_assumed_supported(self):
        with self.assertRaises(ValueError): evidence.attestation_mode('', 'User', 'true')
        with self.assertRaises(ValueError): evidence.attestation_mode('true', '', 'true')

    def test_policy_cli_emits_unavailable_for_private_repository(self):
        with tempfile.TemporaryDirectory(prefix='v3-attestation-policy-') as directory:
            output = pathlib.Path(directory)/'output'
            result = subprocess.run([sys.executable, str(ROOT/'tools/ci/release-evidence.py'), 'policy'],
                env={**os.environ, 'REPOSITORY_PRIVATE': 'true', 'REPOSITORY_OWNER_TYPE': 'User',
                     'ATTESTATION_ENABLED': 'true', 'GITHUB_OUTPUT': str(output)}, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(output.read_text(), 'mode=unavailable\n')

    def test_optional_action_uses_policy_output_without_ignoring_failures(self):
        step = RELEASE.split('      - name: Attest image digest', 1)[1].split('\n      - ', 1)[0]
        self.assertIn("if: steps.attestation-policy.outputs.mode == 'enabled'", step)
        self.assertNotIn('continue-on-error', RELEASE)
        self.assertIn('ATTESTATION_MODE: ${{ steps.attestation-policy.outputs.mode }}', RELEASE)
        self.assertIn('ATTESTATION_OUTCOME: ${{ steps.attest.outcome }}', RELEASE)

    def test_scan_is_unconditional_and_high_critical_including_unfixed_still_block(self):
        step = RELEASE.split('      - name: Scan before publishing;', 1)[1].split('\n      - ', 1)[0]
        self.assertIn('id: scan', step)
        for setting in ('scanners: vuln,secret', 'severity: HIGH,CRITICAL', 'ignore-unfixed: false', "exit-code: '1'"):
            self.assertIn(setting, step)
        self.assertIn('version: v0.74.0', step)
        self.assertIn('format: json', step)
        self.assertIn('output: .artifacts/release/${{ matrix.component }}-security.json', step)
        self.assertNotIn('if:', step)
        self.assertNotRegex(step, r'ignore-policy:|trivyignores:|skip-files:|skip-dirs:')
        self.assertLess(RELEASE.index('id: scan'), RELEASE.index('id: publish'))
        publish = RELEASE.split('      - name: Publish scanned image', 1)[1].split('\n      - ', 1)[0]
        self.assertNotIn('if:', publish)  # Implicit success(): failed scan cannot publish.

    def test_failure_upload_is_separate_from_successful_release_artifacts(self):
        step = RELEASE.split('      - name: Upload failure diagnostics only', 1)[1].split('\n  migrations:', 1)[0]
        self.assertIn('if: failure()', step)
        self.assertIn('name: v3-diagnostics-${{ matrix.component }}', step)
        self.assertIn('path: .artifacts/diagnostics/${{ matrix.component }}-diagnostics.json', step)
        self.assertIn('if-no-files-found: error', step)
        self.assertNotIn('path: .artifacts/release/', step)
        self.assertIn("pattern: 'v3-image-*'", RELEASE)


class ImageEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix='v3-private-release-test-')
        self.addCleanup(self.directory.cleanup)
        self.root = pathlib.Path(self.directory.name)
        self.images = self.root/'release/images'; self.images.mkdir(parents=True)
        self.env = {'GITHUB_SHA': 'a'*40, 'GITHUB_REPOSITORY': 'hamiki4/WeymelaV3',
                    'GITHUB_RUN_ID': '123', 'GITHUB_RUN_ATTEMPT': '1', 'COMPONENT': 'web',
                    'BUILD_OUTCOME': 'success', 'SCAN_OUTCOME': 'success',
                    'PUBLISH_OUTCOME': 'success', 'SBOM_OUTCOME': 'success',
                    'ATTESTATION_MODE': 'unavailable', 'ATTESTATION_OUTCOME': 'skipped'}
        for component in ('api', 'worker', 'web'):
            image = {'component': component, 'commit': self.env['GITHUB_SHA'],
                     'image': f'ghcr.io/hamiki4/weymela-v3-{component}:v3-test',
                     'digest': f'ghcr.io/hamiki4/weymela-v3-{component}@sha256:'+'1'*64,
                     'imageId': 'sha256:'+'2'*64}
            self.write(f'{component}-image', image)
            self.write(f'{component}-security', {'SchemaVersion': 2, 'Metadata': {'ImageID': image['imageId']}, 'Results': []})
            self.write(f'{component}-sbom', {'bomFormat': 'CycloneDX', 'specVersion': '1.6'})
            evidence.complete(self.images, {**self.env, 'COMPONENT': component})
        migrations = self.root/'release/migrations'; migrations.mkdir()
        (migrations/'migration-manifest.json').write_text(json.dumps({'commit': self.env['GITHUB_SHA'], 'files': {}}))

    def write(self, name, value):
        (self.images/f'{name}.json').write_text(json.dumps(value))

    def complete(self, **overrides):
        return evidence.complete(self.images, {**self.env, **overrides})

    def test_private_release_records_unavailable_not_attested(self):
        image = self.complete()
        self.assertEqual(image['attestation']['status'], 'unavailable')
        self.assertNotIn('url', image['attestation'])
        self.assertNotIn('id', image['attestation'])
        self.assertEqual(image['scan']['status'], 'passed')
        self.assertEqual(image['sbom']['path'], 'images/web-sbom.json')

    def test_disabled_supported_repository_records_not_enabled(self):
        self.assertEqual(self.complete(ATTESTATION_MODE='not-enabled')['attestation']['status'], 'not-enabled')

    def test_enabled_attestation_records_real_reference_only_after_success(self):
        result = self.complete(ATTESTATION_MODE='enabled', ATTESTATION_OUTCOME='success', ATTESTATION_ID='42',
                               ATTESTATION_URL='https://github.com/hamiki4/WeymelaV3/attestations/42')
        self.assertEqual(result['attestation']['status'], 'attested')
        self.assertEqual(result['attestation']['id'], '42')

    def test_enabled_attestation_failure_cannot_fall_back_to_unavailable(self):
        for outcome in ('failure', 'cancelled', 'skipped'):
            with self.subTest(outcome=outcome), self.assertRaises(ValueError):
                self.complete(ATTESTATION_MODE='enabled', ATTESTATION_OUTCOME=outcome)

    def test_success_without_persisted_attestation_reference_is_rejected(self):
        with self.assertRaises(ValueError): self.complete(ATTESTATION_MODE='enabled', ATTESTATION_OUTCOME='success')

    def test_disabled_attestation_cannot_claim_action_success(self):
        with self.assertRaises(ValueError): self.complete(ATTESTATION_OUTCOME='success')

    def assert_stage_blocks(self, stage):
        before = (self.images/'web-image.json').read_bytes()
        for outcome in ('failure', 'cancelled', 'skipped', ''):
            with self.subTest(outcome=outcome), self.assertRaises(ValueError):
                self.complete(**{stage + '_OUTCOME': outcome})
        self.assertEqual((self.images/'web-image.json').read_bytes(), before)

    def test_build_failure_cannot_record_releasable_image(self): self.assert_stage_blocks('BUILD')
    def test_scan_failure_cannot_record_releasable_image(self): self.assert_stage_blocks('SCAN')
    def test_publish_failure_cannot_record_releasable_image(self): self.assert_stage_blocks('PUBLISH')
    def test_sbom_failure_cannot_record_releasable_image(self): self.assert_stage_blocks('SBOM')

    def test_blocking_vulnerability_in_report_cannot_be_marked_passed(self):
        for severity in ('HIGH', 'CRITICAL'):
            self.write('web-security', {'SchemaVersion': 2, 'Results': [{'Vulnerabilities': [{'Severity': severity}]}]})
            with self.assertRaises(ValueError): self.complete()

    def test_blocking_secret_in_report_cannot_be_marked_passed(self):
        self.write('web-security', {'SchemaVersion': 2, 'Results': [{'Secrets': [{'Severity': 'HIGH'}]}]})
        with self.assertRaises(ValueError): self.complete()

    def test_malformed_scan_is_not_a_clean_scan(self):
        self.write('web-security', {})
        with self.assertRaises(ValueError): self.complete()

    def test_scan_for_different_image_is_rejected(self):
        self.write('web-security', {'SchemaVersion': 2, 'Metadata': {'ImageID': 'sha256:'+'3'*64}, 'Results': []})
        with self.assertRaises(ValueError): self.complete()

    def test_diagnostics_keep_complete_vulnerability_versions_but_redact_secret_match(self):
        finding = {'VulnerabilityID': 'CVE-test-only', 'PkgName': 'fixture-package', 'Severity': 'HIGH',
                   'InstalledVersion': '1.0', 'FixedVersion': '1.1', 'Status': 'fixed'}
        self.write('web-security', {'SchemaVersion': 2, 'Results': [{'Target': 'fixture-image', 'Class': 'os-pkgs',
            'Type': 'alpine', 'Vulnerabilities': [finding], 'Secrets': [{'RuleID': 'test-rule', 'Severity': 'HIGH',
            'Match': 'sensitive-fixture-match', 'Code': {'Lines': [{'Content': 'sensitive-fixture-context'}]}}]}]})
        result = evidence.diagnostics(self.images, self.root/'diagnostics', {**self.env, 'SCAN_OUTCOME': 'failure'})
        self.assertEqual(result['trivy']['Results'][0]['Vulnerabilities'], [finding])
        self.assertEqual(result['outcomes']['scan'], 'failure')
        self.assertNotIn('sensitive-fixture', json.dumps(result))
        self.assertEqual(result['publishedImage']['digest'], self.complete_metadata()['digest'])

    def complete_metadata(self):
        return json.loads((self.images/'web-image.json').read_text())

    def test_missing_scan_diagnostics_explicitly_require_full_job_log(self):
        result = evidence.diagnostics(self.root, self.root/'diagnostics', self.env)
        self.assertIn('Not a clean scan', result['scanReportError'])
        self.assertEqual(result['scanReportStatus'], 'missing')
        self.assertNotIn('trivy', result)

    def test_invalid_report_shapes_still_produce_safe_failure_diagnostics(self):
        for scan in (None, [], {}, {'SchemaVersion': 2, 'Results': 7},
                     {'SchemaVersion': 2, 'Results': [None]},
                     {'SchemaVersion': 2, 'Results': [{'Secrets': {}}]},
                     {'SchemaVersion': 2, 'Results': [{'Secrets': ['private-fixture-value']}]},
                     {'SchemaVersion': 2, 'Results': [{'Vulnerabilities': ['private-fixture-value']}]},
                     {'SchemaVersion': 2, 'Results': [{'Secrets': [{'Severity': 'HIGH', 'Match': 7}]}]}):
            with self.subTest(scan=scan):
                self.write('web-security', scan)
                result = evidence.diagnostics(self.images, self.root/'diagnostics', self.env)
                self.assertEqual(result['scanReportStatus'], 'invalid')
                self.assertIn('Not a clean scan', result['scanReportError'])
                self.assertNotIn('private-fixture-value', json.dumps(result))

    def test_truncated_json_is_retained_as_an_error_not_printed(self):
        (self.images/'web-security.json').write_text('{"Match":"private-fixture-value"')
        result = evidence.diagnostics(self.images, self.root/'diagnostics', self.env)
        self.assertEqual(result['scanReportStatus'], 'invalid')
        self.assertNotIn('private-fixture-value', json.dumps(result))

    def test_unreadable_report_preserves_safe_error_evidence(self):
        with patch.object(pathlib.Path, 'read_text', side_effect=PermissionError('private-fixture-value')):
            result = evidence.diagnostics(self.root, self.root/'diagnostics', self.env)
        self.assertEqual(result['scanReportStatus'], 'unreadable')
        self.assertNotIn('private-fixture-value', json.dumps(result))

    def test_secret_only_report_is_distinguished_from_vulnerability_finding(self):
        self.write('web-security', {'SchemaVersion': 2, 'Results': [{'Class': 'secret',
            'Target': 'fixture-file', 'Secrets': [{'RuleID': 'fixture-rule', 'Severity': 'HIGH', 'Match': 'private-fixture-value'}]}]})
        result = evidence.diagnostics(self.images, self.root/'diagnostics', {**self.env, 'SCAN_OUTCOME': 'failure'})
        self.assertEqual(result['findingCounts'], {'vulnerabilities': {'HIGH': 0, 'CRITICAL': 0}, 'secrets': {'HIGH': 1, 'CRITICAL': 0}})
        self.assertEqual(result['scanAssessment'], 'blocking-findings-present')

    def test_failed_scan_without_findings_is_not_reported_as_clean(self):
        result = evidence.diagnostics(self.images, self.root/'diagnostics', {**self.env, 'SCAN_OUTCOME': 'failure'})
        self.assertEqual(result['scanAssessment'], 'failure-without-retained-blocking-findings')
        self.assertIn('Not a clean scan', result['scanReportError'])

    def test_omitted_empty_results_does_not_turn_failed_step_into_success(self):
        self.write('web-security', {'SchemaVersion': 2})
        result = evidence.diagnostics(self.images, self.root/'diagnostics', {**self.env, 'SCAN_OUTCOME': 'failure'})
        self.assertEqual(result['scanReportStatus'], 'present')
        self.assertEqual(result['scanAssessment'], 'failure-without-retained-blocking-findings')

    def test_os_evidence_retained_without_image_environment_values(self):
        operating_system = {'Family': 'alpine', 'Name': '3.24.1', 'Eosl': False}
        self.write('web-security', {'SchemaVersion': 2, 'Metadata': {'OS': operating_system,
            'ImageConfig': {'Env': ['private-fixture-value']}}, 'Results': []})
        result = evidence.diagnostics(self.images, self.root/'diagnostics', self.env)
        self.assertEqual(result['trivy']['OS'], operating_system)
        self.assertNotIn('private-fixture-value', json.dumps(result))

    def test_matches_repeated_in_retained_fields_are_redacted_too(self):
        marker = 'private-fixture-value'
        self.write('web-security', {'SchemaVersion': 2, 'Results': [{'Class': 'secret',
            'Target': 'fixture-' + marker, 'Secrets': [{'Severity': 'HIGH', 'Match': marker, 'Title': 'rule ' + marker}]}]})
        result = evidence.diagnostics(self.images, self.root/'diagnostics', self.env)
        self.assertNotIn(marker, json.dumps(result))
        self.assertIn('[REDACTED]', json.dumps(result))

    def test_nonzero_scan_then_real_diagnostics_cli_retains_report_without_secret_output(self):
        # Simulate the scanner's write-report-then-exit-1 behavior, not a real
        # scan. The diagnostics command is the actual workflow command.
        raw = {'SchemaVersion': 2, 'Results': [{'Class': 'os-pkgs', 'Type': 'alpine',
               'Vulnerabilities': [{'VulnerabilityID': 'CVE-test-only', 'PkgName': 'fixture',
               'Severity': 'CRITICAL', 'InstalledVersion': '1', 'FixedVersion': '2'}]},
               {'Class': 'secret', 'Target': 'fixture-file', 'Secrets': [{'RuleID': 'fixture-rule',
                'Severity': 'HIGH', 'Match': 'private-fixture-value', 'Code': {'Lines': [{'Content': 'private-fixture-context'}]}}]}]}
        runner = self.root/'runner'; runner.mkdir()
        reports = runner/'.artifacts/release'; reports.mkdir(parents=True)
        scanner = subprocess.run([sys.executable, '-c',
            "import pathlib,sys; pathlib.Path('.artifacts/release/web-security.json').write_text(sys.stdin.read()); sys.exit(1)"],
            input=json.dumps(raw), cwd=runner, capture_output=True, text=True)
        self.assertEqual(scanner.returncode, 1)
        workflow_step = RELEASE.split('      - name: Preserve failed-image diagnostics', 1)[1].split('\n      - ', 1)[0]
        self.assertIn('if: failure()', workflow_step)
        command = next(line.strip().removeprefix('run: ') for line in workflow_step.splitlines() if line.strip().startswith('run: '))
        args = command.split(); args[0] = sys.executable; args[1] = str(ROOT/args[1])
        diagnostic = subprocess.run(args, cwd=runner, env={**os.environ, **self.env,
            'SCAN_OUTCOME': 'failure', 'PUBLISH_OUTCOME': 'skipped'}, capture_output=True, text=True)
        self.assertEqual(diagnostic.returncode, 0, diagnostic.stderr)
        path = runner/'.artifacts/diagnostics/web-diagnostics.json'
        retained = json.loads(path.read_text())
        self.assertEqual(retained['outcomes']['scan'], 'failure')
        self.assertEqual(retained['outcomes']['publish'], 'skipped')
        self.assertEqual(retained['trivy']['Results'][0]['Vulnerabilities'], raw['Results'][0]['Vulnerabilities'])
        self.assertEqual(retained['findingCounts']['vulnerabilities']['CRITICAL'], 1)
        self.assertEqual(retained['findingCounts']['secrets']['HIGH'], 1)
        self.assertNotIn('private-fixture', path.read_text() + diagnostic.stdout + diagnostic.stderr)
        self.assertIn('v3-diagnostics-web', diagnostic.stdout)

    def manifest(self):
        body = RELEASE.split('- name: Assemble complete release record', 1)[1].split("python3 - <<'PY'\n", 1)[1].split('\n          PY', 1)[0]
        return subprocess.run([sys.executable, '-c', textwrap.dedent(body)], cwd=self.root,
                              env={**os.environ, **self.env}, capture_output=True, text=True)

    def test_manifest_contains_three_digests_checksums_and_accurate_evidence(self):
        result = self.manifest()
        self.assertEqual(result.returncode, 0, result.stderr)
        manifest = json.loads((self.root/'release/release-manifest.json').read_text())
        self.assertEqual(manifest['releaseId'], 'v3-'+'a'*40+'-run123-attempt1')
        self.assertEqual(len(manifest['images']), 3)
        self.assertIn('migrations/migration-manifest.json', manifest['checksums'])
        for image in manifest['images']:
            self.assertEqual(image['attestation']['status'], 'unavailable')
            self.assertEqual(image['scan']['sha256'], manifest['checksums'][image['scan']['path']])
            self.assertEqual(image['sbom']['sha256'], manifest['checksums'][image['sbom']['path']])

    def test_manifest_rejects_any_component_with_failed_build_scan_or_publish(self):
        for component in ('api', 'worker', 'web'):
            original = json.loads((self.images/f'{component}-image.json').read_text())
            for field in ('buildStatus', 'publishStatus', 'scan'):
                changed = copy.deepcopy(original)
                if field == 'scan': changed[field]['status'] = 'failed'
                else: changed[field] = 'failed'
                self.write(f'{component}-image', changed)
                with self.subTest(component=component, field=field): self.assertNotEqual(self.manifest().returncode, 0)
            self.write(f'{component}-image', original)

    def test_manifest_rejects_missing_image(self):
        (self.images/'web-image.json').rename(self.images/'web-incomplete.json')
        self.assertNotEqual(self.manifest().returncode, 0)

    def test_manifest_rejects_migration_from_different_commit(self):
        (self.root/'release/migrations/migration-manifest.json').write_text(json.dumps({'commit': 'b'*40}))
        self.assertNotEqual(self.manifest().returncode, 0)

    def test_manifest_rejects_tampered_scan_or_sbom(self):
        for suffix in ('security', 'sbom'):
            path = self.images/f'web-{suffix}.json'
            before = path.read_bytes()
            path.write_text('{}')
            with self.subTest(suffix=suffix): self.assertNotEqual(self.manifest().returncode, 0)
            path.write_bytes(before)

    def test_manifest_rejects_false_attestation_claim_without_reference(self):
        image = self.complete_metadata(); image['attestation'] = {'status': 'attested'}
        self.write('web-image', image)
        self.assertNotEqual(self.manifest().returncode, 0)


if __name__ == '__main__':
    unittest.main()
