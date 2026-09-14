"""Offline preparation checks. No image builds, live DB or service changes."""
import importlib.util
import copy
import hashlib
import json
import os
import pathlib
import re
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('safety', ROOT / 'tools/ci/repository-safety.py')
safety = importlib.util.module_from_spec(spec)
spec.loader.exec_module(safety)
preflight_spec = importlib.util.spec_from_file_location('preflight', ROOT / 'tools/ci/pilot-preflight.py')
preflight = importlib.util.module_from_spec(preflight_spec)
preflight_spec.loader.exec_module(preflight)
release_spec = importlib.util.spec_from_file_location('release', ROOT / 'tools/ci/verify-release.py')
release = importlib.util.module_from_spec(release_spec)
release_spec.loader.exec_module(release)

class RepositoryGateTests(unittest.TestCase):
    def test_candidate_contains_no_forbidden_files_or_whitespace(self):
        self.assertEqual(safety.inventory(ROOT)['errors'], [])

    def test_populated_environment_is_rejected(self):
        with tempfile.TemporaryDirectory(prefix='v3-prep-fixture-') as directory:
            (pathlib.Path(directory) / '.env').write_text('EXAMPLE=value\n')
            self.assertTrue(safety.inventory(pathlib.Path(directory))['errors'])

    def test_private_material_and_backups_are_rejected(self):
        with tempfile.TemporaryDirectory(prefix='v3-prep-fixture-') as directory:
            (pathlib.Path(directory) / 'example.dump').write_bytes(b'not a real backup')
            self.assertTrue(safety.inventory(pathlib.Path(directory))['errors'])

    def test_source_migration_order_is_exactly_approved_through_phase9a1(self):
        paths = (ROOT / 'src/Weymela.Infrastructure/Persistence/Migrations').glob('[0-9]*.cs')
        actual = sorted(p.stem for p in paths if not p.name.endswith('.Designer.cs'))
        self.assertEqual(actual, ['20260911225904_InitialV3Schema', '20260911233032_AddViewRewardsQrAndPayouts', '20260912011149_AddOperationalSecurityAndNotifications', '20260913045523_AddAuthenticationRecovery', '20260913054814_AddRoleEnrollments', '20260913062900_AddPhoneLoginAliases', '20260914022116_AddDevicePinSessionFoundation'])

    def test_external_actions_are_pinned_and_no_production_deployment(self):
        for path in (ROOT / '.github/workflows').glob('*.yml'):
            text = path.read_text()
            for action in re.findall(r'uses:\s*([^\s#]+)', text):
                self.assertTrue(action.startswith('./') or re.fullmatch(r'[^@]+@[a-f0-9]{40}', action), action)
            self.assertNotRegex(text, r'(?m)^\s*environment:\s*Production')
            self.assertNotIn('pull_request_target:', text)
            self.assertNotIn('docker compose up', text)
            self.assertNotIn('ssh ', text)

    def test_ci_does_not_upload_browser_identity_control(self):
        text = (ROOT / '.github/workflows/ci.yml').read_text()
        self.assertNotIn('path: .artifacts/', text)
        self.assertIn('.artifacts/ci/*.trx', text)

    def test_web_camera_and_cache_policies(self):
        headers = (ROOT / 'docker/web/security-headers.conf').read_text()
        self.assertIn('camera=(self), microphone=(), geolocation=(), payment=(), usb=()', headers)
        self.assertIn("frame-ancestors 'none'", headers)
        self.assertIn('https://identitytoolkit.googleapis.com', headers)
        self.assertIn('https://securetoken.googleapis.com', headers)
        self.assertNotIn('recaptcha', headers.lower())
        config = (ROOT / 'docker/web/nginx.conf').read_text()
        self.assertIn('proxy_cache_bypass 1', config)
        self.assertIn("add_header Cache-Control 'no-cache'", config)
        self.assertNotIn('$request_uri', config)
        self.assertNotIn('$http_authorization', config)

    def test_runtime_layers_only_copy_published_outputs(self):
        for component in ('api', 'worker', 'web'):
            text = (ROOT / f'docker/Dockerfile.{component}').read_text().split(' AS runtime')[1]
            self.assertNotIn('COPY src/', text)
            self.assertNotIn('node_modules', text)
            self.assertIn('USER ', text)
            self.assertIn('HEALTHCHECK ', text)

class ComposeIsolationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.directory = tempfile.TemporaryDirectory(prefix='v3-compose-fixture-')
        root = pathlib.Path(cls.directory.name)
        (root / 'empty.env').write_text('# dry-parse only\n')
        (root / 'placeholder').write_text('not-a-credential\n')
        env = dict(os.environ)
        env.update({f'V3_{kind}_IMAGE': f'ghcr.io/example/weymela-v3-{kind.lower()}@sha256:' + '0' * 64 for kind in ('API', 'WORKER', 'WEB')})
        env.update({'V3_POSTGRES_IMAGE': 'postgres:17-alpine@sha256:' + '0'*64,
                    'V3_API_ENV_FILE': str(root/'empty.env'), 'V3_WORKER_ENV_FILE': str(root/'empty.env'),
                    'V3_POSTGRES_PASSWORD_FILE': str(root/'placeholder'), 'V3_COOKIE_CERTIFICATE_FILE': str(root/'placeholder'),
                    'V3_COOKIE_KEYS_DIRECTORY': str(root), 'V3_EDGE_SUBNET': '172.30.73.0/24', 'V3_WEB_PROXY_IP': '172.30.73.10'})
        cls.config = json.loads(subprocess.check_output(['docker', 'compose', '-f', str(ROOT/'docker/compose.v3-pilot.yml'), 'config', '--format', 'json'], env=env, text=True))

    @classmethod
    def tearDownClass(cls):
        cls.directory.cleanup()

    def test_compose_is_image_only_and_digest_pinned(self):
        for service in self.config['services'].values():
            self.assertNotIn('build', service)
            self.assertRegex(service['image'], r'@sha256:[a-f0-9]{64}$')

    def test_only_web_exposes_loopback_not_v2_ports(self):
        for name, service in self.config['services'].items():
            if name.endswith('-web'):
                self.assertEqual(service['ports'][0]['host_ip'], '127.0.0.1')
                self.assertEqual(str(service['ports'][0]['published']), '18080')
            else: self.assertNotIn('ports', service)

    def test_database_volume_and_network_are_v3_only(self):
        self.assertEqual(self.config['name'], 'weymela-v3-pilot')
        self.assertTrue(self.config['networks']['data']['internal'])
        self.assertTrue(self.config['volumes']['postgres-data']['external'])
        self.assertNotIn('creatorpay', json.dumps(self.config).lower())

    def test_financial_writes_and_development_identity_are_forced_off(self):
        for suffix in ('api', 'worker'):
            env = self.config['services']['weymela-v3-pilot-'+suffix]['environment']
            self.assertEqual(env['V3__FinancialWritesEnabled'], 'false')
            self.assertEqual(env['V3__EnableDevelopmentIdentity'], 'false')

    def test_every_service_has_health_resource_and_log_limits(self):
        for service in self.config['services'].values():
            self.assertIn('healthcheck', service)
            self.assertGreater(int(service['mem_limit']), 0)
            self.assertGreater(float(service['cpus']), 0)
            self.assertEqual(service['logging']['options'], {'max-size': '10m', 'max-file': '3'})
            self.assertEqual(service['restart'], 'no')

    def test_worker_does_not_mount_api_cookie_secrets(self):
        worker = self.config['services']['weymela-v3-pilot-worker']
        self.assertNotIn('secrets', worker)
        self.assertNotIn('volumes', worker)

    def test_readonly_apps_have_exact_single_bounded_tmpfs(self):
        for part in ('api', 'worker', 'web'):
            app = self.config['services']['weymela-v3-pilot-'+part]
            self.assertTrue(app['read_only'])
            self.assertEqual(app['tmpfs'], ['/tmp:size=32m,mode=1777'])

    def manifest(self):
        return {'commit': 'test-commit', 'images': [{'component': p, 'commit': 'test-commit', 'digest': self.config['services']['weymela-v3-pilot-'+p]['image']} for p in ('api', 'worker', 'web')]}

    def test_preflight_accepts_matching_isolated_manifest(self):
        self.assertEqual(preflight.validate(self.config, self.manifest()), [])

    def test_preflight_rejects_v2_or_mutable_image_reference(self):
        config = copy.deepcopy(self.config)
        config['services']['weymela-v3-pilot-api']['image'] = 'creatorpay-api:latest'
        self.assertTrue(preflight.validate(config, self.manifest()))

    def test_preflight_rejects_foreign_source_manifest(self):
        manifest = self.manifest()
        manifest['images'][0]['commit'] = 'wrong-commit'
        self.assertTrue(preflight.validate(self.config, manifest))

    def test_preflight_rejects_public_listener_and_external_network(self):
        config = copy.deepcopy(self.config)
        config['services']['weymela-v3-pilot-web']['ports'][0]['host_ip'] = '0.0.0.0'
        config['networks']['data']['external'] = True
        self.assertTrue(preflight.validate(config, self.manifest()))

    def test_preflight_rejects_unfreeze(self):
        config = copy.deepcopy(self.config)
        config['services']['weymela-v3-pilot-api']['environment']['V3__FinancialWritesEnabled'] = 'true'
        self.assertTrue(preflight.validate(config, self.manifest()))

class ReleaseIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix='v3-release-fixture-')
        self.addCleanup(self.directory.cleanup)
        self.root = pathlib.Path(self.directory.name)
        (self.root/'images').mkdir()
        (self.root/'migrations').mkdir()
        commit = 'a'*40
        images = [{'component':p,'commit':commit,'digest':f'ghcr.io/example/weymela-v3-{p}@sha256:'+('1'*64)} for p in ('api','worker','web')]
        for image in images:
            (self.root/f"images/{image['component']}-image.json").write_text(json.dumps(image))
        (self.root/'migrations/efbundle').write_text('inert test artifact, not executable')
        (self.root/'migrations/v3-forward.sql').write_text('-- inert fixture')
        migration = {'commit':commit}
        (self.root/'migrations/migration-manifest.json').write_text(json.dumps(migration))
        self.manifest = {'commit':commit,'platform':'linux/amd64','deploymentAuthorized':False,'financialWritesEnabled':False,'images':images,'migrations':migration,'checksums':{p.relative_to(self.root).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in self.root.rglob('*') if p.is_file()}}
        self.write_manifest()

    def write_manifest(self):
        (self.root/'release-manifest.json').write_text(json.dumps(self.manifest))

    def test_complete_release_verifies_without_execution(self):
        self.assertFalse(release.verify(self.root)['deploymentAuthorized'])

    def test_tampered_migration_artifact_is_rejected(self):
        (self.root/'migrations/efbundle').write_text('altered')
        with self.assertRaisesRegex(ValueError, 'checksum'): release.verify(self.root)

    def test_cross_directory_checksum_path_is_rejected(self):
        self.manifest['checksums']['../outside'] = '0'*64
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, 'Unsafe'): release.verify(self.root)

    def test_incomplete_runtime_image_set_is_rejected(self):
        self.manifest['images'].pop()
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, 'Incomplete'): release.verify(self.root)

if __name__ == '__main__':
    unittest.main()
