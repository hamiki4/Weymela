#!/usr/bin/env python3
"""Own only the isolated CI BrowserHost and E2E children; never wait forever."""
import json
import os
import pathlib
import signal
import shutil
import subprocess
import time

RESPONSIVE_TITLE = 'rendered role workspaces at'
RESPONSIVE_VIEWPORTS = ('360x800', '375x812', '390x844', '393x852', '430x932', '768x1024', '1366x768', '1440x900', '1920x1080')

def interrupted(signum, _frame):
    raise SystemExit(128 + signum)

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

def archive_result(root, evidence, label):
    source = root/'.artifacts/phase6-e2e-results.json'
    if not source.exists():
        return None
    destination = evidence/'e2e-runs'/f'{label}.json'
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)
    return destination

def merge_results(root, result_paths):
    if not result_paths:
        return
    documents = [json.loads(path.read_text()) for path in result_paths]
    merged = dict(documents[0])
    merged['suites'] = [suite for document in documents for suite in document.get('suites', [])]
    merged['errors'] = [error for document in documents for error in document.get('errors', [])]
    stats = [document.get('stats', {}) for document in documents]
    merged['stats'] = {
        'startTime': min((item.get('startTime', '') for item in stats), default=''),
        'duration': sum(item.get('duration', 0) for item in stats),
        'expected': sum(item.get('expected', 0) for item in stats),
        'skipped': sum(item.get('skipped', 0) for item in stats),
        'unexpected': sum(item.get('unexpected', 0) for item in stats),
        'flaky': sum(item.get('flaky', 0) for item in stats),
    }
    (root/'.artifacts/phase6-e2e-results.json').write_text(json.dumps(merged, indent=2) + '\n')

def run_host(root, evidence, label, args):
    control = root/'.artifacts/browser-host.json'
    result_file = root/'.artifacts/phase6-e2e-results.json'
    if control.exists():
        raise RuntimeError('Stale browser identity control exists; refusing to use an earlier host.')
    result_file.unlink(missing_ok=True)
    signal.signal(signal.SIGTERM, interrupted)
    signal.signal(signal.SIGINT, interrupted)
    host = tests = None
    result = 1
    started = time.monotonic()
    log_path = evidence/f'browser-host-{label}.log'
    with log_path.open('w') as log:
        try:
            print(f'BrowserHost[{label}]: starting isolated PostgreSQL/migrations/seed (180s deadline)', flush=True)
            host = subprocess.Popen(['dotnet', 'tests/Weymela.BrowserHost/bin/Release/net10.0/Weymela.BrowserHost.dll'], stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            deadline = time.monotonic() + 180
            while True:
                if host.poll() is not None: raise RuntimeError('Isolated BrowserHost exited before readiness.')
                if time.monotonic() >= deadline: raise TimeoutError('Isolated BrowserHost readiness exceeded 180 seconds.')
                try:
                    identity = json.loads(control.read_text())
                    break
                except (FileNotFoundError, json.JSONDecodeError):
                    time.sleep(0.25)
            if not identity.get('url', '').startswith('http://127.0.0.1:'):
                raise RuntimeError('Browser control must point to isolated loopback.')
            ready = time.monotonic()
            command = ['npm', 'run', 'e2e', '--', *args]
            print(f'BrowserHost[{label}]: ready after {ready-started:.1f}s; running {" ".join(args) or "ALL E2E tests"}', flush=True)
            tests = subprocess.Popen(command, cwd=root/'src/Weymela.Web', start_new_session=True)
            while tests.poll() is None:
                if host.poll() is not None: raise RuntimeError('Isolated BrowserHost exited during E2E.')
                time.sleep(0.25)
            result = tests.returncode
            print(f'Browser E2E[{label}]: completed after {time.monotonic()-ready:.1f}s, exit {result}', flush=True)
        except (RuntimeError, TimeoutError):
            print('::error::Isolated browser host failed or exceeded startup deadline. No test success is inferred.', flush=True)
            result = 1
        finally:
            signal.signal(signal.SIGTERM, signal.SIG_IGN)
            signal.signal(signal.SIGINT, signal.SIG_IGN)
            cleanup = time.monotonic()
            tests_stopped = stop_child(tests)
            host_stopped = stop_child(host)
            control.unlink(missing_ok=True)
            if not tests_stopped or not host_stopped:
                print('::error::Forced isolated child shutdown was needed; acceptance fails.', flush=True)
                result = result or 1
            print(f'Browser cleanup[{label}]: {time.monotonic()-cleanup:.1f}s; total {time.monotonic()-started:.1f}s', flush=True)
            signal.signal(signal.SIGTERM, interrupted)
            signal.signal(signal.SIGINT, interrupted)
    return result

def main():
    if os.environ.get('GITHUB_ACTIONS') != 'true':
        raise SystemExit('Browser supervision is restricted to isolated hosted CI.')
    root = pathlib.Path.cwd()
    evidence = root/'.artifacts/ci'
    evidence.mkdir(parents=True, exist_ok=True)
    started = time.monotonic()
    result_paths = []
    file_filter = os.environ.get('V3_E2E_FILE')
    grep_filter = os.environ.get('V3_E2E_GREP')
    if grep_filter:
        args = ([file_filter] if file_filter else []) + ['--grep', grep_filter]
        result = run_host(root, evidence, 'targeted', args)
        archive_result(root, evidence, 'targeted')
    elif file_filter == 'responsive.spec.ts':
        result = 0
        for viewport in RESPONSIVE_VIEWPORTS:
            result = run_host(root, evidence, viewport, [file_filter, '--grep', f'{RESPONSIVE_TITLE} {viewport}'])
            if (path := archive_result(root, evidence, viewport)) is not None:
                result_paths.append(path)
            if result:
                break
        merge_results(root, result_paths)
    else:
        result = run_host(root, evidence, 'nonresponsive', ['--grep-invert', RESPONSIVE_TITLE])
        if (path := archive_result(root, evidence, 'nonresponsive')) is not None:
            result_paths.append(path)
        if result == 0:
            for viewport in RESPONSIVE_VIEWPORTS:
                result = run_host(root, evidence, viewport, ['--grep', f'{RESPONSIVE_TITLE} {viewport}'])
                if (path := archive_result(root, evidence, viewport)) is not None:
                    result_paths.append(path)
                if result:
                    break
        merge_results(root, result_paths)
    with (evidence/'stage-timings.tsv').open('a') as timing:
        timing.write(f'browser\tisolated harness total\t{time.monotonic()-started:.1f}\t{result}\n')
    return result

if __name__ == '__main__':
    raise SystemExit(main())
