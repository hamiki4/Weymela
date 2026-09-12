"""Targeted CI tests: execute dispatch/gates/child cleanup, never product suites or Docker."""
import importlib.util
import json
import os
import pathlib
import re
import signal
import subprocess
import sys
import tempfile
import textwrap
import time
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
CI = (ROOT/'.github/workflows/ci.yml').read_text()
RELEASE = (ROOT/'.github/workflows/release.yml').read_text()
REQUIRED = {'source-security', 'dotnet-unit', 'postgres', 'http-api', 'frontend', 'dotnet-checks', 'browser'}
spec = importlib.util.spec_from_file_location('browser_host', ROOT/'tools/ci/browser-host.py')
browser = importlib.util.module_from_spec(spec)
spec.loader.exec_module(browser)

class WorkflowGateTests(unittest.TestCase):
    def gate(self, results):
        # Execute the actual YAML gate body, not a copy of its success predicate.
        body = CI.rsplit("python3 - <<'PY'\n", 1)[1].split('\n          PY', 1)[0]
        with tempfile.TemporaryDirectory(prefix='v3-ci-gate-') as directory:
            output = pathlib.Path(directory)/'output'
            result = subprocess.run([sys.executable, '-c', textwrap.dedent(body)], env={**os.environ, 'VALIDATION_RESULTS': json.dumps(results), 'GITHUB_OUTPUT': str(output)}, capture_output=True, text=True)
            return result.returncode, output.read_text() if output.exists() else ''

    def results(self):
        return {name: {'result': 'success'} for name in REQUIRED}

    def test_all_required_jobs_succeed_emits_explicit_pass(self):
        self.assertEqual(self.gate(self.results()), (0, 'passed=true\n'))

    def test_any_failed_suite_blocks_gate(self):
        for name in REQUIRED:
            with self.subTest(job=name):
                results = self.results(); results[name]['result'] = 'failure'
                code, output = self.gate(results)
                self.assertNotEqual(code, 0); self.assertEqual(output, '')

    def test_skipped_suite_is_not_success(self):
        results = self.results(); results['browser']['result'] = 'skipped'
        self.assertNotEqual(self.gate(results)[0], 0)

    def test_cancelled_suite_is_not_success(self):
        results = self.results(); results['postgres']['result'] = 'cancelled'
        self.assertNotEqual(self.gate(results)[0], 0)

    def test_missing_suite_is_not_success(self):
        results = self.results(); del results['http-api']
        self.assertNotEqual(self.gate(results)[0], 0)

    def test_all_independent_jobs_are_parallel_and_hosted(self):
        jobs = dict(re.findall(r'^  ([a-z-]+):\n(.*?)(?=^  [a-z-]+:\n|\Z)', CI.split('jobs:\n', 1)[1], re.M | re.S))
        self.assertEqual(set(jobs), REQUIRED | {'acceptance'})
        for name in REQUIRED:
            self.assertIn('runs-on: ubuntu-24.04', jobs[name])
            self.assertNotIn('\n    needs:', jobs[name])
        dependencies = re.search(r'needs: \[([^]]+)\]', jobs['acceptance'])[1]
        self.assertEqual(set(dependencies.split(', ')), REQUIRED)

    def test_release_requires_success_output_in_every_downstream_path(self):
        jobs = dict(re.findall(r'^  ([a-z-]+):\n(.*?)(?=^  [a-z-]+:\n|\Z)', RELEASE.split('jobs:\n', 1)[1], re.M | re.S))
        for name in ('images', 'migrations', 'manifest'):
            self.assertIn("needs.validate.outputs.passed == 'true'", jobs[name])
        self.assertIn('needs: validate', jobs['images'])
        self.assertIn('needs: validate', jobs['migrations'])
        self.assertIn('needs: [validate, images, migrations]', jobs['manifest'])
        self.assertNotIn('continue-on-error:', CI + RELEASE)

    def test_browser_installer_has_own_deadline_and_raw_control_is_not_uploaded(self):
        self.assertIn('timeout-minutes: 10\n        working-directory: src/Weymela.Web\n        run: npx playwright install --with-deps chromium', CI)
        self.assertNotRegex(CI, r'(?m)^\s+\.artifacts/browser-host\.(json|log)$')
        self.assertEqual(CI.count('name: v3-'), 7)

class DispatcherTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix='v3-ci-dispatch-')
        self.addCleanup(self.directory.cleanup)
        self.root = pathlib.Path(self.directory.name)
        self.bin = self.root/'bin'; self.bin.mkdir()
        # Stub only the heavyweight executables; Python/bash/timing logic really execute.
        stub = '#!'+sys.executable+'\n'+textwrap.dedent('''\
            import json,os,pathlib,sys,time,signal
            call=[pathlib.Path(sys.argv[0]).name]+sys.argv[1:]
            with open(os.environ['CI_CALLS'],'a') as out: out.write(json.dumps(call)+'\\n')
            if call[0]=='dotnet' and call[-1].endswith('Weymela.BrowserHost.dll'):
                signal.signal(signal.SIGINT,signal.SIG_IGN)
                signal.signal(signal.SIGTERM,lambda *_:sys.exit(0))
                pathlib.Path('.artifacts/browser-host.json').write_text(json.dumps({'url':'http://127.0.0.1:1'}))
                while True: time.sleep(0.01)
            if '--vulnerable' in call: print(json.dumps({'projects':[]}))
            if os.environ.get('CI_FAIL_MATCH') and os.environ['CI_FAIL_MATCH'] in ' '.join(call): sys.exit(7)
            ''')
        for executable in ('dotnet', 'npm', 'node'):
            path = self.bin/executable; path.write_text(stub); path.chmod(0o700)
        self.calls = self.root/'calls.jsonl'
        self.env = {**os.environ, 'GITHUB_ACTIONS': 'true', 'PATH': str(self.bin)+os.pathsep+os.environ['PATH'], 'CI_CALLS': str(self.calls), 'CI_FAIL_MATCH': ''}
        # The real dependency-gate is exercised, with the stub's empty valid audit report.
        (self.root/'tools/ci').mkdir(parents=True)
        (self.root/'tools/ci/dependency-gate.py').write_bytes((ROOT/'tools/ci/dependency-gate.py').read_bytes())

    def run_suite(self, suite):
        result = subprocess.run(['bash', str(ROOT/'tools/ci/validate.sh'), suite], cwd=self.root, env=self.env, capture_output=True, text=True, timeout=10)
        calls = [json.loads(line) for line in self.calls.read_text().splitlines()] if self.calls.exists() else []
        return result, calls

    def test_unit_dispatch_runs_complete_domain_and_application_projects(self):
        result, calls = self.run_suite('unit')
        self.assertEqual(result.returncode, 0, result.stderr)
        tests = [call for call in calls if call[:2] == ['dotnet', 'test']]
        self.assertEqual([x[2] for x in tests], [f'tests/Weymela.{name}/Weymela.{name}.csproj' for name in ('Domain.Tests','Application.Tests')])
        self.assertNotIn('--filter', sum(tests, []))

    def test_postgres_dispatch_runs_all_infrastructure_tests(self):
        result, calls = self.run_suite('postgres')
        self.assertEqual(result.returncode, 0)
        self.assertEqual(len([c for c in calls if c[:2]==['dotnet','test']]), 1)
        self.assertIn('tests/Weymela.Infrastructure.Tests/Weymela.Infrastructure.Tests.csproj', calls[-1])

    def test_http_dispatch_runs_all_api_tests(self):
        result, calls = self.run_suite('http')
        self.assertEqual(result.returncode, 0)
        self.assertIn('tests/Weymela.Api.IntegrationTests/Weymela.Api.IntegrationTests.csproj', calls[-1])

    def test_frontend_dispatch_preserves_vitest_audit_and_build(self):
        result, calls = self.run_suite('frontend')
        self.assertEqual(result.returncode, 0)
        self.assertEqual([c[3:] for c in calls], [['ci','--no-fund'],['audit','--audit-level=high'],['test'],['run','build']])

    def test_release_ef_dependency_and_license_checks_are_retained(self):
        result, calls = self.run_suite('dotnet-checks')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(['dotnet','build','Weymela.slnx','--configuration','Release','--no-restore','--nologo'], calls)
        self.assertTrue(any('has-pending-model-changes' in c for c in calls))
        self.assertTrue(any('--vulnerable' in c and '--include-transitive' in c for c in calls))
        self.assertEqual(calls[-1], ['node','tools/acceptance/dependency-inventory.mjs'])

    def test_test_failure_is_preserved_and_later_commands_do_not_run(self):
        self.env['CI_FAIL_MATCH'] = 'dotnet test'
        result, calls = self.run_suite('unit')
        self.assertEqual(result.returncode, 7)
        self.assertEqual(len(calls), 3)
        self.assertIn('\t7\n', (self.root/'.artifacts/ci/stage-timings.tsv').read_text())

    def test_unknown_or_missing_suite_cannot_succeed(self):
        self.assertNotEqual(self.run_suite('unknown')[0].returncode, 0)
        self.assertFalse(self.calls.exists())

    def browser_run(self, failure=False):
        (self.root/'src/Weymela.Web').mkdir(parents=True)
        if failure: self.env['CI_FAIL_MATCH']='npm run e2e'
        return subprocess.run([sys.executable, str(ROOT/'tools/ci/browser-host.py')], cwd=self.root, env=self.env, capture_output=True, text=True, timeout=8)

    def test_browser_supervisor_runs_all_e2e_and_shuts_down_sigint_ignoring_host(self):
        result = self.browser_run()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('Browser cleanup:', result.stdout)
        self.assertIn(['npm','run','e2e'], [json.loads(x) for x in self.calls.read_text().splitlines()])

    def test_browser_supervisor_preserves_e2e_failure(self):
        result = self.browser_run(failure=True)
        self.assertEqual(result.returncode, 7, result.stdout + result.stderr)

class ChildCleanupTests(unittest.TestCase):
    def test_unresponsive_owned_child_is_killed_within_deadline_and_fails_cleanup(self):
        code = "import signal,time; signal.signal(signal.SIGTERM,signal.SIG_IGN); print('ready',flush=True); time.sleep(60)"
        process = subprocess.Popen([sys.executable,'-c',code], start_new_session=True, stdout=subprocess.PIPE, text=True)
        try:
            self.assertEqual(process.stdout.readline().strip(), 'ready')
            started = time.monotonic()
            self.assertFalse(browser.stop_child(process, grace=0.05))
            self.assertLess(time.monotonic()-started, 3)
            self.assertIsNotNone(process.poll())
        finally:
            if process.poll() is None: os.killpg(process.pid, signal.SIGKILL); process.wait(timeout=5)
            process.stdout.close()

if __name__ == '__main__':
    unittest.main()
