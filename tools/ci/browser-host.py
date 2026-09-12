#!/usr/bin/env python3
"""Own only the isolated CI BrowserHost and E2E children; never wait forever."""
import json
import os
import pathlib
import signal
import subprocess
import time

def stop_child(process, grace=30):
    """TERM the dedicated group, then KILL after a bounded grace period.

    Return False if forced cleanup was needed: that must fail acceptance, not mask it.
    """
    if process is None or process.poll() is not None:
        return True
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        process.wait(timeout=5)
        return True
    try:
        process.wait(timeout=grace)
        return True
    except subprocess.TimeoutExpired:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        process.wait(timeout=5)
        return False

def main():
    if os.environ.get('GITHUB_ACTIONS') != 'true':
        raise SystemExit('Browser supervision is restricted to isolated hosted CI.')
    root = pathlib.Path.cwd()
    control = root/'.artifacts/browser-host.json'
    if control.exists():
        raise SystemExit('Stale browser identity control exists; refusing to use an earlier host.')
    evidence = root/'.artifacts/ci'
    evidence.mkdir(parents=True, exist_ok=True)
    host = tests = None
    result = 1
    started = time.monotonic()
    def interrupted(signum, _frame):
        raise SystemExit(128 + signum)
    signal.signal(signal.SIGTERM, interrupted)
    signal.signal(signal.SIGINT, interrupted)
    with (evidence/'browser-host.log').open('w') as log:
        try:
            print('BrowserHost: starting isolated PostgreSQL/migrations/seed (180s deadline)', flush=True)
            host = subprocess.Popen(['dotnet', 'tests/Weymela.BrowserHost/bin/Release/net10.0/Weymela.BrowserHost.dll'], stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            deadline = time.monotonic() + 180
            while True:
                if host.poll() is not None: raise RuntimeError('Isolated BrowserHost exited before readiness.')
                if time.monotonic() >= deadline: raise TimeoutError('Isolated BrowserHost readiness exceeded 180 seconds.')
                try:
                    identity = json.loads(control.read_text())
                    break
                except (FileNotFoundError, json.JSONDecodeError):
                    # File creation can become visible before its small async write completes.
                    time.sleep(0.25)
            if not identity.get('url', '').startswith('http://127.0.0.1:'):
                raise RuntimeError('Browser control must point to isolated loopback.')
            ready = time.monotonic()
            print(f'BrowserHost: ready after {ready-started:.1f}s; running ALL E2E tests', flush=True)
            tests = subprocess.Popen(['npm', 'run', 'e2e'], cwd=root/'src/Weymela.Web', start_new_session=True)
            while tests.poll() is None:
                if host.poll() is not None: raise RuntimeError('Isolated BrowserHost exited during E2E.')
                time.sleep(0.25)
            result = tests.returncode
            print(f'Browser E2E: completed after {time.monotonic()-ready:.1f}s, exit {result}', flush=True)
        except (RuntimeError, TimeoutError):
            # No raw host/control output: it can contain transient identity/provider data.
            print('::error::Isolated browser host failed or exceeded startup deadline. No test success is inferred.', flush=True)
            result = 1
        finally:
            # Ignore repeat cancellation during the bounded cleanup, not during the suite.
            signal.signal(signal.SIGTERM, signal.SIG_IGN)
            signal.signal(signal.SIGINT, signal.SIG_IGN)
            cleanup = time.monotonic()
            tests_stopped = stop_child(tests)
            host_stopped = stop_child(host)
            if not tests_stopped or not host_stopped:
                print('::error::Forced isolated child shutdown was needed; acceptance fails.', flush=True)
                result = result or 1
            print(f'Browser cleanup: {time.monotonic()-cleanup:.1f}s; total {time.monotonic()-started:.1f}s', flush=True)
            with (evidence/'stage-timings.tsv').open('a') as timing:
                timing.write(f'browser\ttotal including cleanup\t{time.monotonic()-started:.1f}\t{result}\n')
    return result

if __name__ == '__main__':
    raise SystemExit(main())
