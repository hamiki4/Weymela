"""Web package-pin regressions. Execute real shell guards with inert tools, not images."""
import json
import os
import pathlib
import subprocess
import sys
import tempfile
import textwrap
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
DOCKERFILE = (ROOT / 'docker/Dockerfile.web').read_text()
BUILD = ROOT / 'tools/ci/build-image.sh'
BASE = 'nginx:stable-alpine@sha256:dc5069ad14f19660b141b21236140b91656bf89bbc3e2417c70ae650cd66104c'
APK_SHA256 = '8306e5bb577696c9069fe1dfd9e1dcc39d2d481c6a1b0e707fd03c3e21aa6aa2'
APK_URL = 'https://dl-cdn.alpinelinux.org/alpine/v3.24/main/x86_64/libuuid-2.42.3-r1.apk'


class ShellFixture(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory(prefix='v3-web-security-test-')
        self.addCleanup(directory.cleanup)
        self.root = pathlib.Path(directory.name)
        self.bin = self.root / 'bin'
        self.bin.mkdir()
        self.calls_file = self.root / 'calls.jsonl'
        self.env = {**os.environ, 'PATH': str(self.bin) + os.pathsep + os.environ['PATH'],
                    'WEB_TEST_CALLS': str(self.calls_file), 'WEB_TEST_FAIL': '',
                    'WEB_TEST_PACKAGE': 'libuuid-2.42.3-r1', 'WEB_TEST_EXISTING': '',
                    'GITHUB_ACTIONS': 'true', 'GITHUB_REF': 'refs/heads/main',
                    'GITHUB_SHA': 'a' * 40, 'GITHUB_REPOSITORY_OWNER': 'HamiKi4',
                    'GITHUB_REPOSITORY': 'hamiki4/WeymelaV3', 'GITHUB_RUN_ID': '456',
                    'GITHUB_RUN_ATTEMPT': '1', 'GITHUB_OUTPUT': str(self.root / 'output')}
        stub = '#!' + sys.executable + '\n' + textwrap.dedent('''\
            import json,os,pathlib,sys
            args=sys.argv[1:]
            tool=pathlib.Path(sys.argv[0]).name
            stage=tool
            if tool=='docker' and args[:2]==['buildx','build']: stage='build'
            if tool=='docker' and args[:1]==['run']: stage='runtime'
            if tool=='apk' and args[:1]==['add']: stage='install'
            if tool=='apk' and args[:1]==['info']: stage='floor'
            call={'tool':tool,'args':args,'stage':stage}
            if tool=='sha256sum': call['stdin']=sys.stdin.read()
            with open(os.environ['WEB_TEST_CALLS'],'a') as stream:
                stream.write(json.dumps(call)+'\\n')
            if os.environ['WEB_TEST_FAIL']==stage: sys.exit(8)
            if tool=='git': print('a'*40)
            elif tool=='docker' and args[:3]==['buildx','imagetools','inspect']:
                if args[3].startswith('ghcr.io/'):
                    sys.exit(0 if os.environ['WEB_TEST_EXISTING'] else 1)
                print(json.dumps({'digest':'sha256:'+'b'*64}))
            elif stage=='runtime':
                print('musl-1.2.5-r0')
                print(os.environ['WEB_TEST_PACKAGE'])
            ''')
        # No real network, install, image build, runtime or removal can execute.
        for name in ('wget', 'sha256sum', 'apk', 'rm', 'docker', 'git'):
            path = self.bin / name
            path.write_text(stub)
            path.chmod(0o700)

    def calls(self):
        return [json.loads(line) for line in self.calls_file.read_text().splitlines()] if self.calls_file.exists() else []

    def run_install(self, failure=''):
        runtime = DOCKERFILE.split('FROM ${WEB_IMAGE} AS runtime\n', 1)[1]
        body = runtime.split('RUN ', 1)[1].split('\nCOPY ', 1)[0]
        return subprocess.run(['sh', '-c', body], cwd=self.root,
                              env={**self.env, 'WEB_TEST_FAIL': failure},
                              text=True, capture_output=True, timeout=10)

    def run_build(self, component='web', **env):
        return subprocess.run(['bash', str(BUILD), component], cwd=self.root,
                              env={**self.env, **env}, text=True, capture_output=True, timeout=10)


class RuntimePackagePatchTests(ShellFixture):
    def test_only_fixed_signed_apk_is_installed_offline_after_checksum(self):
        result = self.run_install()
        self.assertEqual(result.returncode, 0, result.stderr)
        calls = self.calls()
        self.assertEqual([call['tool'] for call in calls], ['wget', 'sha256sum', 'apk', 'apk', 'rm'])
        self.assertEqual(calls[0]['args'], ['-O', '/tmp/weymela-libuuid-2.42.3-r1.apk', APK_URL])
        self.assertEqual(calls[1]['args'], ['-c', '-'])
        self.assertEqual(calls[1]['stdin'], APK_SHA256 + '  /tmp/weymela-libuuid-2.42.3-r1.apk\n')
        self.assertEqual(calls[2]['args'], ['add', '--no-cache', '--no-network', '/tmp/weymela-libuuid-2.42.3-r1.apk'])
        self.assertEqual(calls[3]['args'], ['info', '--installed', 'libuuid=2.42.3-r1'])
        self.assertEqual(calls[4]['args'], ['/tmp/weymela-libuuid-2.42.3-r1.apk'])

    def test_download_failure_cannot_install_package(self):
        self.assertNotEqual(self.run_install('wget').returncode, 0)
        self.assertEqual([call['tool'] for call in self.calls()], ['wget'])

    def test_checksum_mismatch_cannot_install_package(self):
        self.assertNotEqual(self.run_install('sha256sum').returncode, 0)
        self.assertNotIn('apk', [call['tool'] for call in self.calls()])

    def test_signature_or_dependency_failure_blocks_runtime_layer(self):
        self.assertNotEqual(self.run_install('install').returncode, 0)
        self.assertEqual(self.calls()[-1]['stage'], 'install')

    def test_installed_version_check_failure_is_not_ignored(self):
        self.assertNotEqual(self.run_install('floor').returncode, 0)
        self.assertEqual(self.calls()[-1]['stage'], 'floor')

    def test_no_general_upgrade_or_signature_bypass_and_nginx_stays_nonroot(self):
        runtime = DOCKERFILE.split('FROM ${WEB_IMAGE} AS runtime\n', 1)[1]
        self.assertNotRegex(runtime, r'--allow-untrusted|--force|--no-check-certificate|apk (?:upgrade|update)')
        self.assertLess(runtime.index('apk add'), runtime.index('USER 101:101'))
        self.assertIn('RUN npm run build', DOCKERFILE)
        self.assertIn('COPY docker/web/security-headers.conf', runtime)
        self.assertIn('HEALTHCHECK ', runtime)


class HostedRuntimeGateTests(ShellFixture):
    def test_web_uses_locked_base_and_preserves_immutable_amd64_load_build(self):
        result = self.run_build()
        self.assertEqual(result.returncode, 0, result.stderr)
        calls = self.calls()
        build = next(call['args'] for call in calls if call['stage'] == 'build')
        self.assertIn('WEB_IMAGE=' + BASE, build)
        self.assertIn('NODE_IMAGE=node:24-bookworm-slim@sha256:' + 'b' * 64, build)
        self.assertIn('--load', build)
        self.assertIn('--provenance=false', build)
        self.assertEqual(build[build.index('--platform') + 1], 'linux/amd64')
        self.assertEqual(build[build.index('--tag') + 1],
                         'ghcr.io/hamiki4/weymela-v3-web:v3-' + 'a' * 40 + '-run456-attempt1')
        self.assertNotIn('nginx:stable-alpine', [call['args'][3] for call in calls
                         if call['args'][:3] == ['buildx', 'imagetools', 'inspect']])
        self.assertIn(BASE, (self.root / '.artifacts/release/web-bases.txt').read_text())

    def test_runtime_gate_inspects_exact_built_image_without_network_or_writes(self):
        result = self.run_build()
        self.assertEqual(result.returncode, 0, result.stderr)
        calls = self.calls()
        runtime = next(call['args'] for call in calls if call['stage'] == 'runtime')
        built = next(call['args'] for call in calls if call['stage'] == 'build')
        self.assertEqual(runtime, ['run', '--rm', '--network', 'none', '--read-only', '--cap-drop', 'ALL',
                                  '--security-opt', 'no-new-privileges', '--entrypoint', '/sbin/apk',
                                  built[built.index('--tag') + 1], 'info', '-v'])
        self.assertIn('libuuid-2.42.3-r1\n', (self.root / '.artifacts/release/web-runtime-packages.txt').read_text())
        self.assertTrue((self.root / 'output').read_text().startswith('image=ghcr.io/'))

    def test_vulnerable_runtime_version_cannot_emit_successful_build_outputs(self):
        result = self.run_build(WEB_TEST_PACKAGE='libuuid-2.42.1-r0')
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.root / 'output').exists())

    def test_r0_is_insufficient_for_cve_2026_78408(self):
        result = self.run_build(WEB_TEST_PACKAGE='libuuid-2.42.3-r0')
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.root / 'output').exists())

    def test_missing_libuuid_cannot_emit_successful_build_outputs(self):
        self.assertNotEqual(self.run_build(WEB_TEST_PACKAGE='').returncode, 0)
        self.assertFalse((self.root / 'output').exists())

    def test_image_build_failure_blocks_runtime_gate_and_outputs(self):
        self.assertNotEqual(self.run_build(WEB_TEST_FAIL='build').returncode, 0)
        self.assertNotIn('runtime', [call['stage'] for call in self.calls()])
        self.assertFalse((self.root / 'output').exists())

    def test_runtime_inspection_failure_blocks_build_outputs(self):
        self.assertNotEqual(self.run_build(WEB_TEST_FAIL='runtime').returncode, 0)
        self.assertFalse((self.root / 'output').exists())

    def test_non_ci_environment_cannot_build(self):
        self.assertNotEqual(self.run_build(GITHUB_ACTIONS='false').returncode, 0)
        self.assertEqual(self.calls(), [])

    def test_existing_release_tag_is_never_overwritten(self):
        self.assertNotEqual(self.run_build(WEB_TEST_EXISTING='true').returncode, 0)
        self.assertNotIn('build', [call['stage'] for call in self.calls()])

    def test_api_worker_do_not_install_or_inspect_web_package(self):
        for component in ('api', 'worker'):
            with self.subTest(component=component):
                result = self.run_build(component)
                self.assertEqual(result.returncode, 0, result.stderr)
        self.assertNotIn('runtime', [call['stage'] for call in self.calls()])
        for call in self.calls():
            if call['stage'] == 'build':
                self.assertFalse(any(arg.startswith(('WEB_IMAGE=', 'NODE_IMAGE=')) for arg in call['args']))

    def test_mandatory_scan_and_private_evidence_workflow_are_still_fail_closed(self):
        workflow = (ROOT / '.github/workflows/release.yml').read_text()
        scan = workflow.split('      - name: Scan before publishing;', 1)[1].split('\n      - ', 1)[0]
        for value in ('scanners: vuln,secret', 'severity: HIGH,CRITICAL', 'ignore-unfixed: false', "exit-code: '1'"):
            self.assertIn(value, scan)
        self.assertNotIn('if:', scan)
        self.assertNotIn('continue-on-error:', workflow)
        self.assertIn('needs: [validate, images, migrations]', workflow)
        self.assertIn("if: steps.attestation-policy.outputs.mode == 'enabled'", workflow)
        self.assertIn('path: .artifacts/diagnostics/${{ matrix.component }}-diagnostics.json', workflow)


if __name__ == '__main__':
    unittest.main()
