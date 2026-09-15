#!/usr/bin/env python3
"""Read-only Compose + release-manifest verification; never pulls or starts services."""
import argparse
import json
import pathlib
import re
import subprocess
import base64
import stat
import hmac

PLACEHOLDERS = ('placeholder', 'replace-me', 'change-me', 'example', 'external', 'test-only', 'not-a-live')

def _secret_bytes(value, minimum=32):
    try:
        decoded = base64.b64decode(value or '', validate=True)
        return (base64.b64encode(decoded).decode('ascii') == value
                and len(decoded) >= minimum and len(set(decoded)) >= 16
                and decoded != bytes(range(1, 33))
                and not any(word.encode() in decoded.lower() for word in PLACEHOLDERS))
    except (ValueError, TypeError):
        return False

def _secret_sources(service):
    return {
        item if isinstance(item, str) else item.get('source', '')
        for item in service.get('secrets', [])
    }

def _valid_firebase_credential(config):
    try:
        path = pathlib.Path(config['secrets']['v3-firebase-admin.json']['file'])
        if not path.is_absolute() or path.is_symlink() or not path.is_file():
            return False
        details = path.stat()
        if stat.S_IMODE(details.st_mode) & 0o077 or details.st_size < 100 or details.st_size > 64 * 1024:
            return False
        value = json.loads(path.read_text())
        pem_header = '-----BEGIN ' + 'PRIVATE KEY-----'
        return (value.get('type') == 'service_account'
                and value.get('project_id') == 'weymela-pilot'
                and isinstance(value.get('client_email'), str) and value['client_email'].endswith('.iam.gserviceaccount.com')
                and isinstance(value.get('private_key'), str) and value['private_key'].startswith(pem_header))
    except (KeyError, OSError, UnicodeError, json.JSONDecodeError):
        return False

def _valid_secret_metadata(config, name):
    try:
        path = pathlib.Path(config['secrets'][name]['file'])
        details = path.lstat()
        return (path.is_absolute() and not path.is_symlink() and stat.S_ISREG(details.st_mode)
                and stat.S_IMODE(details.st_mode) == 0o400)
    except (KeyError, OSError):
        return False

def _key_directory_source(service):
    for item in service.get('volumes', []):
        if item.get('type') == 'bind' and item.get('target') == '/run/weymela-v3/keys':
            return pathlib.Path(item.get('source', ''))
    return None

def validate(config, manifest):
    errors = []
    expected = {f'weymela-v3-pilot-{part}' for part in ('api', 'worker', 'web', 'postgres')}
    if config.get('name') != 'weymela-v3-pilot' or set(config.get('services', {})) != expected:
        return ['Unexpected Compose project/services; V3-only required.']
    images = {item['component']: item for item in manifest.get('images', [])}
    if set(images) != {'api', 'worker', 'web'}: errors.append('Complete three-image release manifest required.')
    for name, service in config['services'].items():
        part = name.removeprefix('weymela-v3-pilot-')
        expression = (r'postgres:17-alpine@sha256:[a-f0-9]{64}' if part == 'postgres' else rf'ghcr\.io/[a-z0-9_.-]+/weymela-v3-{part}@sha256:[a-f0-9]{{64}}')
        if not re.fullmatch(expression, service.get('image', '')): errors.append(f'{part}: exact approved V3 digest reference required.')
        if 'build' in service or service.get('privileged') or service.get('network_mode') == 'host': errors.append(f'{part}: unsafe runtime mode.')
        if part in images:
            approved_digest = images[part]['digest']
            image_id = images[part].get('imageId')
            if (image_id and re.fullmatch(r'sha256:[a-f0-9]{64}', image_id)
                    and service.get('image') == approved_digest.split('@', 1)[0] + '@' + image_id):
                errors.append(f'{part}: imageId is not a registry digest reference.')
            if approved_digest != service['image'] or images[part]['commit'] != manifest.get('commit'):
                errors.append(f'{part}: source/digest does not match manifest.')
        if part != 'web' and service.get('ports'): errors.append(f'{part}: host port forbidden.')
        if part == 'web' and any(p.get('host_ip') != '127.0.0.1' or str(p.get('published')) != '18080' for p in service.get('ports', [])):
            errors.append('Web must use only approved loopback port18080.')
        if part in ('api', 'worker'):
            environment = service.get('environment', {})
            if environment.get('V3__FinancialWritesEnabled') != 'false' or environment.get('V3__EnableDevelopmentIdentity') != 'false':
                errors.append(f'{part}: preparation must stay frozen with Development identity disabled.')
    api = config['services']['weymela-v3-pilot-api']
    api_environment = api.get('environment', {})
    required_api = {
        'V3__Auth__Provider': 'Firebase',
        'V3__Auth__EmailDeliveryMode': 'Resend',
        'V3__Auth__FirebaseCustomTokenMode': 'FirebaseAdmin',
        'V3__Auth__FirebaseProjectId': 'weymela-pilot',
        'V3__Auth__ResendFromAddress': 'no-reply@pilot-mail.weymela.com',
        'V3__Auth__ResendFromName': 'Weymela Pilot',
        'GOOGLE_APPLICATION_CREDENTIALS': '/run/secrets/v3-firebase-admin.json',
        'V3__Auth__CookieKeyDirectory': '/run/weymela-v3/keys',
        'V3__Auth__CookieCertificatePath': '/run/secrets/v3-cookie-protection.pfx',
        'V3__AllowedOrigins__0': 'https://v3-pilot.weymela.com',
        'V3__PublicWebUrl': 'https://v3-pilot.weymela.com',
        'V3__PublicApiUrl': 'https://api-v3-pilot.weymela.com',
        'V3__Security__TlsEdgeConfirmed': 'true',
        'V3__FinancialWritesEnabled': 'false',
        'V3__RateLimitMultiplier': '1',
    }
    for key, expected_value in required_api.items():
        if api_environment.get(key) != expected_value:
            errors.append(f'api: invalid or missing Pilot authentication setting {key}.')
    resend_key = api_environment.get('V3__Auth__ResendApiKey', '')
    if (not re.fullmatch(r're_[A-Za-z0-9_-]{17,253}', resend_key)
            or any(word in resend_key.lower() for word in PLACEHOLDERS)):
        errors.append('api: protected Resend API key is missing or malformed.')
    code_key = api_environment.get('V3__Auth__CodeHashKey', '')
    pin_key = api_environment.get('V3__Auth__PinPepper', '')
    for key, value in (('V3__Auth__CodeHashKey', code_key), ('V3__Auth__PinPepper', pin_key)):
        if not _secret_bytes(value): errors.append(f'api: {key} must contain strict base64-encoded 32+ byte secret material.')
    if code_key and pin_key and hmac.compare_digest(code_key, pin_key):
        errors.append('api: auth code and PIN secrets must be independent.')
    certificate_password = api_environment.get('V3__Auth__CookieCertificatePassword', '')
    if (len(certificate_password) < 16 or len(certificate_password) > 512
            or any(word in certificate_password.lower() for word in PLACEHOLDERS)):
        errors.append('api: protected cookie certificate password is missing.')
    if 'v3-firebase-admin.json' not in _secret_sources(api):
        errors.append('api: read-only Firebase Admin credential secret is required.')
    elif not _valid_firebase_credential(config):
        errors.append('api: external Firebase Admin credential file is missing, unsafe, or structurally invalid.')
    if 'v3-cookie-protection.pfx' not in _secret_sources(api) or not _valid_secret_metadata(config, 'v3-cookie-protection.pfx'):
        errors.append('api: external cookie protection certificate metadata is missing or unsafe.')
    key_directory = _key_directory_source(api)
    try:
        key_details = key_directory.lstat() if key_directory is not None else None
        if (key_details is None or key_directory.is_symlink() or not stat.S_ISDIR(key_details.st_mode)
                or stat.S_IMODE(key_details.st_mode) != 0o700):
            errors.append('api: persistent cookie key directory metadata is missing or unsafe.')
    except OSError:
        errors.append('api: persistent cookie key directory metadata is missing or unsafe.')
    for part in ('worker', 'web'):
        service = config['services'][f'weymela-v3-pilot-{part}']
        environment = service.get('environment', {})
        forbidden = ('V3__Auth__ResendApiKey', 'V3__Auth__CodeHashKey', 'V3__Auth__PinPepper',
                     'V3__Auth__CookieCertificatePassword', 'GOOGLE_APPLICATION_CREDENTIALS')
        secret_sources = _secret_sources(service)
        if (any(key in environment for key in forbidden)
                or {'v3-firebase-admin.json', 'v3-cookie-protection.pfx'} & secret_sources
                or any(item.get('target') == '/run/weymela-v3/keys' for item in service.get('volumes', []))):
            errors.append(f'{part}: API authentication secrets are forbidden.')
    worker_environment = config['services']['weymela-v3-pilot-worker'].get('environment', {})
    if worker_environment.get('V3__Auth__Provider') != 'Firebase' or worker_environment.get('V3__Auth__FirebaseProjectId') != 'weymela-pilot':
        errors.append('worker: approved public Firebase project identity is required.')
    web_image = images.get('web', {})
    if web_image.get('firebaseProjectId') != api_environment.get('V3__Auth__FirebaseProjectId'):
        errors.append('web/api: Firebase project identities do not match.')
    if not config.get('networks', {}).get('data', {}).get('internal'): errors.append('V3 data network must be internal.')
    for network in config.get('networks', {}).values():
        if not network.get('name', '').startswith('weymela-v3-pilot-') or network.get('external'):
            errors.append('No existing/external V2 network is permitted.')
    for volume in config.get('volumes', {}).values():
        if volume.get('name') != 'weymela-v3-pilot-postgres-data': errors.append('Unexpected data volume.')
    return errors

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--env-file', required=True, help='Protected V3-only interpolation file, outside source')
    parser.add_argument('--manifest', required=True)
    args = parser.parse_args()
    root = pathlib.Path(__file__).resolve().parents[2]
    # Never print expanded config: it contains runtime credentials from env_file.
    process = subprocess.run(['docker', 'compose', '--env-file', args.env_file, '-f', str(root/'docker/compose.v3-pilot.yml'), 'config', '--format', 'json'], text=True, capture_output=True)
    if process.returncode: raise SystemExit('Compose configuration unavailable/invalid. Review protected V3 files privately; output redacted.')
    errors = validate(json.loads(process.stdout), json.loads(pathlib.Path(args.manifest).read_text()))
    print(json.dumps({'readOnly': True, 'deploymentAuthorized': False, 'errors': errors}, indent=2))
    raise SystemExit(bool(errors))
