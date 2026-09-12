#!/usr/bin/env python3
"""Read-only candidate inventory; optional generated JSON report. Never prints file contents."""
import argparse
import hashlib
import json
import pathlib
import re
import subprocess

SKIP = {'.git', '.artifacts', 'artifacts', 'bin', 'obj', 'node_modules', 'dist', 'test-results', 'playwright-report', 'coverage', '__pycache__'}
FORBIDDEN = re.compile(r'(^|/)(secrets|backups|releases|screenshots|reference-images)(/|$)|\.(dump|backup|pfx|p12|jks|keystore|pem|key|tar|tgz|zip|log|trx)$|\.tar\.gz$|firebase-adminsdk|service-account', re.I)
PRIVATE = re.compile(rb'-----BEGIN (?:RSA |EC |OPENSSH |ENCRYPTED )?PRIVATE KEY-----|"private_key"\s*:\s*"[^"\s]|AIza[0-9A-Za-z_-]{35}|gh[pousr]_[A-Za-z0-9]{30,}')

def inventory(root):
    root = root.resolve()
    # Include tracked files even if ignored, preventing accidental `git add -f` bypass.
    if (root / '.git').exists():
        names = subprocess.check_output(['git', '-C', str(root), 'ls-files', '-z', '--cached', '--others', '--exclude-standard']).decode().split('\0')
        paths = [root / name for name in sorted(set(names)) if name]
    else:
        paths = sorted(p for p in root.rglob('*') if p.is_file() and not any(part in SKIP for part in p.relative_to(root).parts))
    entries, errors = [], []
    for path in paths:
        name = path.relative_to(root).as_posix()
        if not path.is_file() or path.is_symlink():
            errors.append(f'{name}: missing file or symlink'); continue
        if FORBIDDEN.search(name) or any(part in SKIP for part in path.relative_to(root).parts):
            errors.append(f'{name}: forbidden artifact')
        if '.env' in path.name and not path.name.endswith('.env.example'):
            errors.append(f'{name}: non-template environment file')
        if path.name in ('appsettings.Pilot.json', 'appsettings.Production.json', 'appsettings.Local.json'):
            errors.append(f'{name}: populated environment-specific configuration')
        if 'CreatorPay' in name or 'V2' in name.split('/'):
            errors.append(f'{name}: V2 source path')
        content = path.read_bytes()
        if PRIVATE.search(content):
            errors.append(f'{name}: possible credential/private key')
        if path.name.endswith('.env.example'):
            for line in content.decode().splitlines():
                if not line or line.startswith('#') or '=' not in line: continue
                key, value = line.split('=', 1)
                if re.search(r'Password$|ConnectionStrings__|AccessKey$|Token$|Secret$', key, re.I) and value.strip():
                    errors.append(f'{name}: credential template value is not blank')
        try:
            decoded = content.decode('utf-8')
            for number, line in enumerate(decoded.splitlines(), 1):
                if line.rstrip(' \t') != line:
                    errors.append(f'{name}:{number}: trailing whitespace')
            if '\r' in decoded:
                errors.append(f'{name}: CRLF; use LF')
        except UnicodeDecodeError:
            if not name.startswith('src/Weymela.Web/public/') or path.suffix not in ('.png', '.ico'):
                errors.append(f'{name}: unapproved binary source')
        entries.append({'path': name, 'bytes': len(content), 'sha256': hashlib.sha256(content).hexdigest()})
    return {'fileCount': len(entries), 'totalBytes': sum(x['bytes'] for x in entries), 'files': entries, 'errors': errors}

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('root', nargs='?', default='.')
    parser.add_argument('--report')
    args = parser.parse_args()
    result = inventory(pathlib.Path(args.root))
    if args.report:
        report = pathlib.Path(args.report)
        report.parent.mkdir(parents=True, exist_ok=True)
        report.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({k: v for k, v in result.items() if k != 'files'}, indent=2))
    raise SystemExit(bool(result['errors']))
