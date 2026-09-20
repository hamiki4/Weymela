"""Offline preparation checks. No image builds, live DB or service changes."""
import importlib.util
import base64
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

    def test_pilot_draft_legal_bundle_is_hash_bound_nonproduction_and_image_packaged(self):
        bundle = ROOT / 'src/Weymela.Api/PilotLegal'
        manifest = json.loads((bundle / 'publication.json').read_text())
        self.assertEqual(manifest['environment'], 'Pilot')
        self.assertEqual(manifest['classification'], 'PilotDraft')
        self.assertFalse(manifest['attorneyReviewed'])
        self.assertFalse(manifest['productionApproved'])
        self.assertFalse(manifest['automaticPublication'])
        self.assertEqual({item['type'] for item in manifest['documents']},
                         {'TermsOfService', 'PrivacyPolicy'})
        for item in manifest['documents']:
            source = ROOT / 'src/Weymela.Web/public/legal' / pathlib.Path(item['contentPath']).name
            self.assertTrue(source.is_file())
            self.assertEqual(item['contentHash'], 'sha256:' + hashlib.sha256(source.read_bytes()).hexdigest())
            text = source.read_text()
            self.assertIn('Pilot draft', text)
            self.assertRegex(text, r'not (?:an )?attorney-reviewed final Production')
            self.assertEqual(item['version'], 'pilot-draft-2026-09-16.1')
            self.assertTrue(item['viewPath'].startswith('/legal/'))

        terms = (ROOT / 'src/Weymela.Web/public/legal/pilot-terms-of-service-v1.txt').read_text().lower()
        for subject in ('account and profile use', 'Responsibilities', 'Prohibited conduct',
                        'Promotions', 'Suspension and termination', 'Pilot service', 'Support'):
            self.assertIn(subject.lower(), terms)
        privacy = (ROOT / 'src/Weymela.Web/public/legal/pilot-privacy-policy-v1.txt').read_text().lower()
        for subject in ('contact information', 'Customer, Creator, and Business profiles',
                        'Promotion participation', 'transaction and QR activity',
                        'Location information', 'social account', 'prevent fraud', 'operate and improve'):
            self.assertIn(subject.lower(), privacy)

        project = (ROOT / 'src/Weymela.Api/Weymela.Api.csproj').read_text()
        dockerfile = (ROOT / 'docker/Dockerfile.api').read_text()
        for filename in ('pilot-terms-of-service-v1.txt', 'pilot-privacy-policy-v1.txt'):
            self.assertIn(filename, project)
        self.assertIn('COPY src/Weymela.Web/public/legal/', dockerfile)

    def test_pilot_legal_publication_is_explicit_target_guarded_and_scope_bounded(self):
        bundle = ROOT / 'src/Weymela.Api/PilotLegal'
        manifest = json.loads((bundle / 'publication.json').read_text())
        sql = (bundle / 'publish.sql').read_text()
        self.assertIn("current_database() <> 'weymela_v3_pilot'", sql)
        self.assertIn('pg_advisory_xact_lock', sql)
        self.assertIn('ON CONFLICT ("Type", "Version") DO NOTHING', sql)
        self.assertIn('BEGIN;', sql)
        self.assertIn('COMMIT;', sql)
        for item in manifest['documents']:
            self.assertIn(item['id'], sql)
            self.assertIn(item['type'], sql)
            self.assertIn(item['version'], sql)
            self.assertIn(item['contentHash'], sql)
        for forbidden_table in ('LegalAcceptances', 'CustomerProfiles', 'IdentityBindings',
                                'FinancialConfigurationVersions'):
            self.assertNotIn(f'v3."{forbidden_table}"', sql)

    def test_source_migration_order_is_exactly_approved_through_phase_i1(self):
        paths = (ROOT / 'src/Weymela.Infrastructure/Persistence/Migrations').glob('[0-9]*.cs')
        actual = sorted(p.stem for p in paths if not p.name.endswith('.Designer.cs'))
        self.assertEqual(actual, ['20260911225904_InitialV3Schema', '20260911233032_AddViewRewardsQrAndPayouts', '20260912011149_AddOperationalSecurityAndNotifications', '20260913045523_AddAuthenticationRecovery', '20260913054814_AddRoleEnrollments', '20260913062900_AddPhoneLoginAliases', '20260914022116_AddDevicePinSessionFoundation', '20260916042557_AddPasswordCredentials', '20260916202055_AddCustomerProfiles', '20260917020034_AddProductHandoffTransactions', '20260917233008_AddBusinessLedPromotionAndUgc', '20260918144832_AddUgcCustomerOffers', '20260919120000_AddUgcCustomerDiscountLimit'])

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

    def test_private_authentication_material_is_excluded_from_images(self):
        ignore = (ROOT / '.dockerignore').read_text()
        self.assertIn('**/*firebase-admin*.json', ignore)
        web = (ROOT / 'docker/Dockerfile.web').read_text()
        self.assertNotIn('GOOGLE_APPLICATION_CREDENTIALS', web)
        self.assertNotIn('ResendApiKey', web)
        worker = (ROOT / 'docker/Dockerfile.worker').read_text()
        self.assertNotIn('firebase-admin', worker.lower())
        self.assertNotIn('resend', worker.lower())

    def test_worker_healthcheck_uses_only_the_atomic_freshness_signal(self):
        dockerfile = (ROOT / 'docker/Dockerfile.worker').read_text()
        health_line = next(line for line in dockerfile.splitlines() if line.startswith('HEALTHCHECK '))
        self.assertIn('CMD ["/bin/sh", "/app/worker-healthcheck.sh"]', health_line)
        self.assertIn('--timeout=8s', health_line)
        self.assertNotIn('dotnet', health_line.lower())
        self.assertNotIn('--check-health', health_line)

        script = (ROOT / 'src/Weymela.Worker/worker-healthcheck.sh').read_text()
        self.assertIn('/tmp/weymela-worker/healthy', script)
        self.assertIn('stat -c %Y', script)
        self.assertIn('interval * 4', script)
        self.assertIn('freshness" -lt 60', script)
        self.assertNotIn('dotnet', script.lower())
        self.assertNotIn('connectionstring', script.lower())

        program = (ROOT / 'src/Weymela.Worker/Program.cs').read_text()
        self.assertNotIn('--check-health', program)
        self.assertIn('healthSignal.MarkSuccessfulCycle()', program)
        self.assertIn('healthSignal.MarkFailedCycle()', program)

        compose = (ROOT / 'docker/compose.pilot.yml').read_text()
        worker = compose.split('  weymela-v3-pilot-worker:', 1)[1].split('  weymela-v3-pilot-web:', 1)[0]
        self.assertIn('cpus: 0.25', worker)
        self.assertIn('mem_limit: 256m', worker)
        self.assertIn('timeout: 8s', worker)
        self.assertIn('test: [CMD, /bin/sh, /app/worker-healthcheck.sh]', worker)
        self.assertNotIn('Weymela.Worker.dll', worker)

class ComposeIsolationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.directory = tempfile.TemporaryDirectory(prefix='v3-compose-fixture-')
        root = pathlib.Path(cls.directory.name)
        code_secret = base64.b64encode(hashlib.sha512(b'compose-code-secret').digest()).decode('ascii')
        pin_secret = base64.b64encode(hashlib.sha512(b'compose-pin-secret').digest()).decode('ascii')
        certificate_password = hashlib.sha256(b'compose-certificate-password').hexdigest()
        database_password = hashlib.sha256(b'compose-database-password').hexdigest()
        (root / 'api.env').write_text(
            'ASPNETCORE_ENVIRONMENT=Pilot\n'
            'DOTNET_ENVIRONMENT=Pilot\n'
            f'ConnectionStrings__WeymelaV3=Host=weymela-v3-pilot-postgres;Database=weymela_v3_pilot;Username=weymela_v3_api;Password={database_password}\n'
            'V3__EnableDevelopmentIdentity=false\n'
            'V3__Auth__Provider=Firebase\n'
            'V3__Auth__EmailDeliveryMode=Resend\n'
            'V3__Auth__FirebaseCustomTokenMode=FirebaseAdmin\n'
            'V3__Auth__FirebaseProjectId=weymela-pilot\n'
            'V3__Auth__ResendApiKey=re_' + hashlib.sha256(b'compose-resend-key').hexdigest() + '\n'
            'V3__Auth__ResendFromAddress=no-reply@pilot-mail.weymela.com\n'
            'V3__Auth__ResendFromName=Weymela Pilot\n'
            f'V3__Auth__CodeHashKey={code_secret}\n'
            f'V3__Auth__PinPepper={pin_secret}\n'
            f'V3__Auth__CookieCertificatePassword={certificate_password}\n'
            'V3__AllowedOrigins__0=https://pilot.weymela.com\n'
            'V3__PublicWebUrl=https://pilot.weymela.com\n'
            'V3__PublicApiUrl=https://pilot.weymela.com\n'
            'V3__Security__CameraPolicy=camera=(self), microphone=(), geolocation=(), payment=(), usb=()\n'
            'V3__Security__TlsEdgeConfirmed=true\n'
            'V3__FinancialWritesEnabled=false\n'
            'V3__Deposits__Mode=ManualApproval\n'
            'V3__Social__Mode=Disabled\n'
            'V3__Push__Enabled=false\n'
            'V3__Worker__Enabled=true\n'
            'V3__Worker__BatchSize=20\n'
            'V3__Worker__RecipientBatchSize=100\n'
            'V3__Worker__IntervalSeconds=5\n'
            'V3__RateLimitMultiplier=1\n')
        (root / 'worker.env').write_text(
            'V3__Auth__Provider=Firebase\n'
            'V3__Auth__FirebaseProjectId=weymela-pilot\n')
        (root / 'placeholder').write_text('not-a-credential\n')
        cookie = root / 'cookie-protection.pfx'
        cookie.write_bytes(b'synthetic-cookie-certificate-fixture')
        cookie.chmod(0o400)
        firebase = root / 'firebase-admin.json'
        firebase.write_text(json.dumps({
            'type': 'service_account', 'project_id': 'weymela-pilot',
            'client_email': 'test@weymela-pilot.iam.gserviceaccount.com',
            'private_key': '-----BEGIN ' + 'PRIVATE KEY-----\ntest-only\n-----END PRIVATE KEY-----\n'}))
        firebase.chmod(0o400)
        root.chmod(0o700)
        env = dict(os.environ)
        env.update({f'V3_{kind}_IMAGE': f'ghcr.io/example/weymela-v3-{kind.lower()}@sha256:' + '0' * 64 for kind in ('API', 'WORKER', 'WEB')})
        env.update({'V3_POSTGRES_IMAGE': 'postgres:17-alpine@sha256:' + '0'*64,
                    'V3_API_ENV_FILE': str(root/'api.env'), 'V3_WORKER_ENV_FILE': str(root/'worker.env'),
                    'V3_POSTGRES_PASSWORD_FILE': str(root/'placeholder'), 'V3_COOKIE_CERTIFICATE_FILE': str(cookie),
                    'V3_FIREBASE_ADMIN_CREDENTIALS_FILE': str(firebase),
                    'V3_COOKIE_KEYS_DIRECTORY': str(root), 'V3_EDGE_SUBNET': '172.30.73.0/24', 'V3_WEB_PROXY_IP': '172.30.73.10'})
        cls.config = json.loads(subprocess.check_output(['docker', 'compose', '-f', str(ROOT/'docker/compose.pilot.yml'), 'config', '--format', 'json'], env=env, text=True))

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

    def test_cookie_certificate_and_persistent_keyring_are_api_only(self):
        api = self.config['services']['weymela-v3-pilot-api']
        self.assertIn('v3-cookie-protection.pfx', {item['source'] for item in api['secrets']})
        key_mount = next(item for item in api['volumes'] if item['target'] == '/run/weymela-v3/keys')
        self.assertEqual(key_mount['type'], 'bind')
        self.assertFalse(key_mount.get('read_only', False))
        for part in ('worker', 'web'):
            service = self.config['services']['weymela-v3-pilot-'+part]
            self.assertNotIn('v3-cookie-protection.pfx', {item['source'] for item in service.get('secrets', [])})
            self.assertNotIn('/run/weymela-v3/keys', {item.get('target') for item in service.get('volumes', [])})

    def test_firebase_admin_and_resend_secrets_are_api_only(self):
        api = self.config['services']['weymela-v3-pilot-api']
        self.assertIn('v3-firebase-admin.json', {item['source'] for item in api['secrets']})
        self.assertEqual(api['environment']['GOOGLE_APPLICATION_CREDENTIALS'], '/run/secrets/v3-firebase-admin.json')
        for part in ('worker', 'web'):
            service = self.config['services']['weymela-v3-pilot-'+part]
            self.assertNotIn('GOOGLE_APPLICATION_CREDENTIALS', service.get('environment', {}))
            self.assertNotIn('V3__Auth__ResendApiKey', service.get('environment', {}))
            self.assertNotIn('v3-firebase-admin.json', {item['source'] for item in service.get('secrets', [])})

    def test_readonly_apps_have_exact_single_bounded_tmpfs(self):
        for part in ('api', 'worker', 'web'):
            app = self.config['services']['weymela-v3-pilot-'+part]
            self.assertTrue(app['read_only'])
            self.assertEqual(app['tmpfs'], ['/tmp:size=32m,mode=1777'])

    def manifest(self):
        images = [{'component': p, 'commit': 'test-commit', 'digest': self.config['services']['weymela-v3-pilot-'+p]['image']} for p in ('api', 'worker', 'web')]
        next(item for item in images if item['component'] == 'web')['firebaseProjectId'] = 'weymela-pilot'
        return {'commit': 'test-commit', 'images': images}

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

    def test_preflight_rejects_image_id_used_as_registry_digest(self):
        config = copy.deepcopy(self.config)
        manifest = self.manifest()
        image = next(item for item in manifest['images'] if item['component'] == 'api')
        image['imageId'] = 'sha256:' + '2' * 64
        config['services']['weymela-v3-pilot-api']['image'] = image['digest'].split('@', 1)[0] + '@' + image['imageId']
        errors = preflight.validate(config, manifest)
        self.assertIn('api: imageId is not a registry digest reference.', errors)

    def test_preflight_rejects_public_listener_and_external_network(self):
        config = copy.deepcopy(self.config)
        config['services']['weymela-v3-pilot-web']['ports'][0]['host_ip'] = '0.0.0.0'
        config['networks']['data']['external'] = True
        self.assertTrue(preflight.validate(config, self.manifest()))

    def test_preflight_rejects_unfreeze(self):
        config = copy.deepcopy(self.config)
        config['services']['weymela-v3-pilot-api']['environment']['V3__FinancialWritesEnabled'] = 'true'
        self.assertTrue(preflight.validate(config, self.manifest()))

    def test_preflight_rejects_disabled_or_mismatched_authentication(self):
        for key, value in (
            ('V3__Auth__EmailDeliveryMode', 'Disabled'),
            ('V3__Auth__FirebaseCustomTokenMode', 'Disabled'),
            ('V3__Auth__FirebaseProjectId', 'wrong-project'),
            ('V3__Auth__ResendApiKey', ''),
            ('V3__Auth__PinPepper', 'weak'),
            ('V3__Auth__CodeHashKey', 'weak')):
            with self.subTest(key=key):
                config = copy.deepcopy(self.config)
                config['services']['weymela-v3-pilot-api']['environment'][key] = value
                self.assertTrue(preflight.validate(config, self.manifest()))

        config = copy.deepcopy(self.config)
        config['services']['weymela-v3-pilot-worker']['environment']['V3__Auth__FirebaseProjectId'] = 'other-project'
        self.assertTrue(preflight.validate(config, self.manifest()))

        config = copy.deepcopy(self.config)
        config['services']['weymela-v3-pilot-api']['environment']['V3__Auth__PinPepper'] = \
            config['services']['weymela-v3-pilot-api']['environment']['V3__Auth__CodeHashKey']
        self.assertTrue(preflight.validate(config, self.manifest()))

    def test_preflight_rejects_web_api_firebase_project_mismatch(self):
        manifest = self.manifest()
        next(item for item in manifest['images'] if item['component'] == 'web')['firebaseProjectId'] = 'other-project'
        self.assertTrue(preflight.validate(self.config, manifest))

    def test_preflight_rejects_missing_or_unsafe_firebase_credential_file(self):
        for path in ('/tmp/does-not-exist-weymela-firebase.json', None):
            config = copy.deepcopy(self.config)
            if path is None:
                path = config['secrets']['v3-firebase-admin.json']['file']
                pathlib.Path(path).chmod(0o644)
            config['secrets']['v3-firebase-admin.json']['file'] = path
            with self.subTest(path=path):
                self.assertTrue(preflight.validate(config, self.manifest()))
            if path != '/tmp/does-not-exist-weymela-firebase.json':
                pathlib.Path(path).chmod(0o400)

    def test_preflight_rejects_unsafe_cookie_certificate_or_key_directory_metadata(self):
        certificate = pathlib.Path(self.config['secrets']['v3-cookie-protection.pfx']['file'])
        certificate.chmod(0o600)
        self.assertTrue(preflight.validate(self.config, self.manifest()))
        certificate.chmod(0o400)

        directory = pathlib.Path(next(item['source'] for item in
            self.config['services']['weymela-v3-pilot-api']['volumes']
            if item['target'] == '/run/weymela-v3/keys'))
        directory.chmod(0o750)
        self.assertTrue(preflight.validate(self.config, self.manifest()))
        directory.chmod(0o700)

class ReleaseIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix='v3-release-fixture-')
        self.addCleanup(self.directory.cleanup)
        self.root = pathlib.Path(self.directory.name)
        (self.root/'images').mkdir()
        (self.root/'migrations').mkdir()
        commit = 'a'*40
        images = [{'component':p,'commit':commit,'digest':f'ghcr.io/example/weymela-v3-{p}@sha256:'+('1'*64)} for p in ('api','worker','web')]
        next(item for item in images if item['component'] == 'web')['firebaseProjectId'] = 'weymela-pilot'
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
