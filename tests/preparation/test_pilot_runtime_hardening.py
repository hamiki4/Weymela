"""Disposable tests for Pilot runtime assembly and metadata contracts."""
import base64
import contextlib
import hashlib
import importlib.util
import io
import os
import pathlib
import stat
import tempfile
import unittest
from unittest import mock


ROOT = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location(
    'pilot_runtime', ROOT / 'tools/ops/pilot-runtime.py')
runtime = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runtime)


def secret(label):
    return base64.b64encode(hashlib.sha512(label.encode('ascii')).digest()).decode('ascii')


def protected_text(label):
    return hashlib.sha256(label.encode('ascii')).hexdigest()


class PilotRuntimeAssemblerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='v3-pilot-runtime-')
        self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name)
        self.base = self.root / 'api.env'
        self.auth = self.root / 'auth.env'
        self.base_values = {
            'ASPNETCORE_ENVIRONMENT': 'Pilot',
            'DOTNET_ENVIRONMENT': 'Pilot',
            'ConnectionStrings__WeymelaV3':
                'Host=weymela-v3-pilot-postgres;Database=weymela_v3_pilot;'
                f'Username=weymela_v3_api;Password={protected_text("database-password")};Maximum Pool Size=20',
            'V3__EnableDevelopmentIdentity': 'false',
            'V3__Auth__Provider': 'Firebase',
            'V3__Auth__FirebaseProjectId': 'weymela-pilot',
            'V3__Auth__CookieKeyDirectory': '/run/weymela-v3/keys',
            'V3__Auth__CookieCertificatePath': '/run/secrets/v3-cookie-protection.pfx',
            'V3__AllowedOrigins__0': 'https://pilot.weymela.com',
            'V3__PublicWebUrl': 'https://pilot.weymela.com',
            'V3__PublicApiUrl': 'https://pilot.weymela.com',
            'V3__Security__CameraPolicy': 'camera=(self), microphone=(), geolocation=(self), payment=(), usb=()',
            'V3__Security__TlsEdgeConfirmed': 'true',
            'V3__FinancialWritesEnabled': 'false',
            'V3__Deposits__Mode': 'ManualApproval',
            'V3__Social__Mode': 'Disabled',
            'V3__Push__Enabled': 'false',
            'V3__Worker__Enabled': 'true',
            'V3__Worker__BatchSize': '20',
            'V3__Worker__RecipientBatchSize': '100',
            'V3__Worker__IntervalSeconds': '5',
            'V3__RateLimitMultiplier': '1',
        }
        self.auth_values = {
            'V3__Auth__FirebaseProjectId': 'weymela-pilot',
            'V3__Auth__EmailDeliveryMode': 'Resend',
            'V3__Auth__ResendApiKey': 're_' + protected_text('resend-key'),
            'V3__Auth__ResendFromAddress': 'no-reply@pilot-mail.weymela.com',
            'V3__Auth__ResendFromName': 'Weymela Pilot',
            'V3__Auth__FirebaseCustomTokenMode': 'FirebaseAdmin',
            'GOOGLE_APPLICATION_CREDENTIALS': '/run/secrets/v3-firebase-admin.json',
            'V3__Auth__CodeHashKey': secret('independent-code-hash-key'),
            'V3__Auth__PinPepper': secret('independent-pin-pepper'),
            'V3__Auth__CookieCertificatePassword': protected_text('certificate-password'),
        }
        self.write_inputs()

    def write_inputs(self):
        self.base.write_text(''.join(f'{key}={value}\n' for key, value in self.base_values.items()))
        self.auth.write_text(''.join(f'{key}={value}\n' for key, value in self.auth_values.items()))
        self.base.chmod(0o600)
        self.auth.chmod(0o600)

    def assert_invalid(self):
        self.write_inputs()
        with self.assertRaises(runtime.ContractError):
            runtime.assemble(self.base, self.auth)

    def test_valid_complete_configuration_is_atomically_written_with_mode_0600(self):
        before = self.base.stat().st_ino
        values = runtime.assemble(self.base, self.auth)
        runtime.write_atomic(self.base, values, os.geteuid(), os.getegid())
        details = self.base.stat()
        self.assertEqual(stat.S_IMODE(details.st_mode), 0o600)
        self.assertEqual((details.st_uid, details.st_gid), (os.geteuid(), os.getegid()))
        self.assertNotEqual(before, details.st_ino)
        rendered = runtime.parse_env(self.base, runtime.OUTPUT_KEYS, 'assembled fixture')
        self.assertEqual(set(rendered), set(runtime.OUTPUT_KEYS))
        self.assertEqual(rendered['V3__FinancialWritesEnabled'], 'false')

    def test_atomic_output_rejects_unsafe_existing_file_or_parent(self):
        values = runtime.assemble(self.base, self.auth)
        self.base.chmod(0o640)
        with self.assertRaises(runtime.ContractError):
            runtime.write_atomic(self.base, values, os.geteuid(), os.getegid())
        self.base.chmod(0o600)
        self.root.chmod(0o777)
        with self.assertRaises(runtime.ContractError):
            runtime.write_atomic(self.base, values, os.geteuid(), os.getegid())

    def test_missing_duplicate_and_unexpected_keys_fail_closed(self):
        public_api = self.base_values.pop('V3__PublicApiUrl')
        self.assert_invalid()
        self.base_values['V3__PublicApiUrl'] = public_api
        self.write_inputs()
        with self.base.open('a') as stream:
            stream.write('V3__PublicApiUrl=https://pilot.weymela.com\n')
        with self.assertRaises(runtime.ContractError):
            runtime.assemble(self.base, self.auth)
        self.write_inputs()
        with self.auth.open('a') as stream:
            stream.write('UNREVIEWED_SECRET=forbidden\n')
        with self.assertRaises(runtime.ContractError):
            runtime.assemble(self.base, self.auth)

    def test_project_sender_financial_and_cryptographic_contracts_fail_closed(self):
        mutations = (
            ('auth', 'V3__Auth__FirebaseProjectId', 'another-project'),
            ('auth', 'V3__Auth__ResendFromAddress', 'no-reply@other.invalid'),
            ('base', 'V3__FinancialWritesEnabled', 'true'),
            ('auth', 'V3__Auth__CodeHashKey', 'not-base64'),
            ('auth', 'V3__Auth__CodeHashKey', base64.b64encode(b'A' * 32).decode('ascii')),
            ('auth', 'V3__Auth__PinPepper', base64.b64encode(bytes(range(1, 33))).decode('ascii')),
        )
        for source, key, value in mutations:
            with self.subTest(key=key, value=value):
                original = dict(self.base_values if source == 'base' else self.auth_values)
                target = self.base_values if source == 'base' else self.auth_values
                target[key] = value
                self.assert_invalid()
                target.clear()
                target.update(original)
        self.auth_values['V3__Auth__PinPepper'] = self.auth_values['V3__Auth__CodeHashKey']
        self.assert_invalid()

    def test_shell_like_or_malformed_input_is_rejected_as_data(self):
        for injected in ('$(id)', '`id`', '${HOME}', 'value\\command'):
            with self.subTest(injected=injected):
                self.auth_values['V3__Auth__CookieCertificatePassword'] = injected
                self.assert_invalid()
        self.write_inputs()
        with self.auth.open('a') as stream:
            stream.write('export V3__Auth__PinPepper=forbidden\n')
        with self.assertRaises(runtime.ContractError):
            runtime.assemble(self.base, self.auth)

    def test_env_inputs_must_be_owner_only_regular_non_symlink_files(self):
        self.auth.chmod(0o640)
        with self.assertRaises(runtime.ContractError):
            runtime.assemble(self.base, self.auth)
        self.auth.unlink()
        target = self.root / 'auth-target.env'
        target.write_text('synthetic fixture')
        target.chmod(0o600)
        self.auth.symlink_to(target)
        with self.assertRaises(runtime.ContractError):
            runtime.assemble(self.base, self.auth)

    def test_cli_output_and_errors_never_emit_secret_values(self):
        secrets = [self.auth_values[key] for key in runtime.SECRET_KEYS if key in self.auth_values]
        secrets.append(self.base_values['ConnectionStrings__WeymelaV3'])
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            result = runtime.main(['assemble-api-env', '--base', str(self.base), '--auth', str(self.auth),
                                   '--output', str(self.base), '--check-only'])
        self.assertEqual(result, 0)
        for value in secrets:
            self.assertNotIn(value, output.getvalue())

        self.auth_values['V3__Auth__CodeHashKey'] = 'malformed-sensitive-input'
        self.write_inputs()
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            result = runtime.main(['assemble-api-env', '--base', str(self.base), '--auth', str(self.auth),
                                   '--output', str(self.base), '--check-only'])
        self.assertEqual(result, 1)
        self.assertNotIn('malformed-sensitive-input', output.getvalue())


class PilotHostMetadataTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='v3-pilot-metadata-')
        self.addCleanup(self.temp.cleanup)
        root = pathlib.Path(self.temp.name)
        self.firebase = root / 'firebase.json'
        self.certificate = root / 'cookie.pfx'
        self.keys = root / 'keys'
        self.firebase.touch(mode=0o400)
        self.certificate.touch(mode=0o400)
        self.keys.mkdir(mode=0o700)
        self.uid, self.gid = os.geteuid(), os.getegid()

    def validate(self, uid=None, gid=None):
        return runtime.validate_host_metadata(
            self.firebase, self.certificate, self.keys,
            self.uid if uid is None else uid, self.gid if gid is None else gid)

    def test_valid_metadata_uses_lstat_without_reading_secret_contents(self):
        with (
            mock.patch.object(pathlib.Path, 'read_bytes', side_effect=AssertionError('secret read')),
            mock.patch.object(pathlib.Path, 'read_text', side_effect=AssertionError('secret read')),
        ):
            self.assertEqual(self.validate(), [])

    def test_wrong_owner_mode_and_unrelated_user_readability_fail(self):
        self.assertTrue(self.validate(uid=self.uid + 1))
        self.firebase.chmod(0o440)
        self.assertTrue(self.validate())
        self.firebase.chmod(0o404)
        self.assertTrue(self.validate())

    def test_symlink_and_key_directory_permissions_fail(self):
        target = self.firebase.with_suffix('.target')
        target.touch(mode=0o400)
        self.firebase.unlink()
        self.firebase.symlink_to(target)
        self.assertTrue(self.validate())
        self.firebase.unlink()
        self.firebase.touch(mode=0o400)
        self.keys.chmod(0o750)
        self.assertTrue(self.validate())


if __name__ == '__main__':
    unittest.main()
