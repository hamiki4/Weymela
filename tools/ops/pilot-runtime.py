#!/usr/bin/env python3
"""Fail-closed Pilot environment assembly and host metadata validation.

This tool never evaluates environment input as shell code. The metadata command
uses lstat only and never opens a protected file.
"""
from __future__ import annotations

import argparse
import base64
import binascii
import hmac
import json
import os
import pathlib
import re
import stat
import tempfile


FIREBASE_ADMIN_HOST = pathlib.Path('/etc/weymela/pilot/firebase-admin.json')
COOKIE_CERTIFICATE_HOST = pathlib.Path('/etc/weymela-v3/pilot/cookie-protection.pfx')
COOKIE_KEYS_HOST = pathlib.Path('/var/lib/weymela-v3/pilot/cookie-keys')

BASE_KEYS = (
    'ASPNETCORE_ENVIRONMENT',
    'DOTNET_ENVIRONMENT',
    'ConnectionStrings__WeymelaV3',
    'V3__EnableDevelopmentIdentity',
    'V3__Auth__Provider',
    'V3__Auth__FirebaseProjectId',
    'V3__Auth__CookieKeyDirectory',
    'V3__Auth__CookieCertificatePath',
    'V3__AllowedOrigins__0',
    'V3__PublicWebUrl',
    'V3__PublicApiUrl',
    'V3__Security__CameraPolicy',
    'V3__Security__TlsEdgeConfirmed',
    'V3__FinancialWritesEnabled',
    'V3__Deposits__Mode',
    'V3__Social__Mode',
    'V3__Push__Enabled',
    'V3__Worker__Enabled',
    'V3__Worker__BatchSize',
    'V3__Worker__RecipientBatchSize',
    'V3__Worker__IntervalSeconds',
    'V3__RateLimitMultiplier',
)

AUTH_KEYS = (
    'V3__Auth__FirebaseProjectId',
    'V3__Auth__EmailDeliveryMode',
    'V3__Auth__ResendApiKey',
    'V3__Auth__ResendFromAddress',
    'V3__Auth__ResendFromName',
    'V3__Auth__FirebaseCustomTokenMode',
    'GOOGLE_APPLICATION_CREDENTIALS',
    'V3__Auth__CodeHashKey',
    'V3__Auth__PinPepper',
    'V3__Auth__CookieCertificatePassword',
)

OUTPUT_KEYS = BASE_KEYS + tuple(key for key in AUTH_KEYS if key not in BASE_KEYS)
SECRET_KEYS = {
    'ConnectionStrings__WeymelaV3',
    'V3__Auth__ResendApiKey',
    'V3__Auth__CodeHashKey',
    'V3__Auth__PinPepper',
    'V3__Auth__CookieCertificatePassword',
}
_KEY = re.compile(r'^[A-Za-z_][A-Za-z0-9_]*$')
_SAFE_VALUE = re.compile(r'^[A-Za-z0-9 _@:/.,;+=()!?%*~^-]+$')
_RESEND_KEY = re.compile(r'^re_[A-Za-z0-9_-]{17,253}$')
_PLACEHOLDERS = ('placeholder', 'replace-me', 'replace_me', 'change-me', 'example', 'external', 'test-only', 'not-a-live')


class ContractError(ValueError):
    """A safe configuration-contract error whose message never contains a value."""


def _safe_value(key: str, value: str) -> str:
    if len(value) > 4096 or not value or not _SAFE_VALUE.fullmatch(value):
        raise ContractError(f'{key}: missing or contains unsupported env-file characters.')
    if '$' in value or '`' in value or '\\' in value or '\n' in value or '\r' in value:
        raise ContractError(f'{key}: shell-like or multiline input is forbidden.')
    return value


def parse_env(path: pathlib.Path, allowed: tuple[str, ...], label: str) -> dict[str, str]:
    try:
        details = path.lstat()
        if (path.is_symlink() or not stat.S_ISREG(details.st_mode)
                or stat.S_IMODE(details.st_mode) & 0o077
                or details.st_uid != os.geteuid()):
            raise ContractError(f'{label}: input must be an owner-only regular non-symlink file.')
        lines = path.read_text(encoding='utf-8').splitlines()
    except ContractError:
        raise
    except (OSError, UnicodeError) as error:
        raise ContractError(f'{label}: input file is unavailable or invalid UTF-8.') from error
    values: dict[str, str] = {}
    allowed_set = set(allowed)
    for number, raw in enumerate(lines, 1):
        if not raw.strip() or raw.lstrip().startswith('#'):
            continue
        if raw != raw.strip() or '=' not in raw:
            raise ContractError(f'{label}: malformed assignment at line {number}.')
        key, value = raw.split('=', 1)
        if not _KEY.fullmatch(key) or key not in allowed_set:
            raise ContractError(f'{label}: unexpected key at line {number}.')
        if key in values:
            raise ContractError(f'{label}: duplicate key {key}.')
        if len(value) >= 2 and value[0] == value[-1] and value[0] in "'\"":
            value = value[1:-1]
        elif value.startswith(("'", '"')) or value.endswith(("'", '"')):
            raise ContractError(f'{label}: malformed quoted value for {key}.')
        values[key] = _safe_value(key, value)
    return values


def _secret_bytes(key: str, value: str) -> bytes:
    try:
        if re.search(r'\s', value):
            raise ValueError
        decoded = base64.b64decode(value, validate=True)
    except (ValueError, binascii.Error) as error:
        raise ContractError(f'{key}: strict base64 secret material is required.') from error
    if base64.b64encode(decoded).decode('ascii') != value:
        raise ContractError(f'{key}: canonical base64 secret material is required.')
    if len(decoded) < 32 or len(set(decoded)) < 16:
        raise ContractError(f'{key}: decoded secret material is too weak.')
    lowered = decoded.lower()
    if decoded == bytes(range(1, 33)) or any(word.encode() in lowered for word in _PLACEHOLDERS):
        raise ContractError(f'{key}: placeholder or deterministic fixture material is forbidden.')
    return decoded


def _require_exact(values: dict[str, str], key: str, expected: str) -> None:
    if values.get(key) != expected:
        raise ContractError(f'{key}: missing or does not match the Pilot contract.')


def _validate_connection_string(value: str) -> None:
    parts: dict[str, str] = {}
    for item in value.split(';'):
        if not item:
            continue
        if '=' not in item:
            raise ContractError('ConnectionStrings__WeymelaV3: malformed PostgreSQL setting.')
        key, setting = item.split('=', 1)
        normalized = key.strip().lower()
        if not normalized or normalized in parts:
            raise ContractError('ConnectionStrings__WeymelaV3: duplicate or malformed PostgreSQL setting.')
        parts[normalized] = setting.strip()
    expected = {
        'host': 'weymela-v3-pilot-postgres',
        'database': 'weymela_v3_pilot',
        'username': 'weymela_v3_api',
    }
    for key, setting in expected.items():
        if parts.get(key) != setting:
            raise ContractError(f'ConnectionStrings__WeymelaV3: invalid {key} setting.')
    password = parts.get('password', '')
    if len(password) < 16 or any(word in password.lower() for word in _PLACEHOLDERS):
        raise ContractError('ConnectionStrings__WeymelaV3: protected database password is missing or unsafe.')


def validate_complete(values: dict[str, str]) -> None:
    missing = [key for key in OUTPUT_KEYS if not values.get(key)]
    if missing:
        raise ContractError(f'Missing required Pilot key: {missing[0]}.')
    expected = {
        'ASPNETCORE_ENVIRONMENT': 'Pilot',
        'DOTNET_ENVIRONMENT': 'Pilot',
        'V3__EnableDevelopmentIdentity': 'false',
        'V3__Auth__Provider': 'Firebase',
        'V3__Auth__FirebaseProjectId': 'weymela-pilot',
        'V3__Auth__EmailDeliveryMode': 'Resend',
        'V3__Auth__ResendFromAddress': 'no-reply@pilot-mail.weymela.com',
        'V3__Auth__ResendFromName': 'Weymela Pilot',
        'V3__Auth__FirebaseCustomTokenMode': 'FirebaseAdmin',
        'GOOGLE_APPLICATION_CREDENTIALS': '/run/secrets/v3-firebase-admin.json',
        'V3__Auth__CookieKeyDirectory': '/run/weymela-v3/keys',
        'V3__Auth__CookieCertificatePath': '/run/secrets/v3-cookie-protection.pfx',
        'V3__AllowedOrigins__0': 'https://pilot.weymela.com',
        'V3__PublicWebUrl': 'https://pilot.weymela.com',
        'V3__PublicApiUrl': 'https://pilot.weymela.com',
        'V3__Security__CameraPolicy': 'camera=(self), microphone=(), geolocation=(), payment=(), usb=()',
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
    for key, expected_value in expected.items():
        _require_exact(values, key, expected_value)
    _validate_connection_string(values['ConnectionStrings__WeymelaV3'])
    resend_key = values['V3__Auth__ResendApiKey']
    if not _RESEND_KEY.fullmatch(resend_key) or any(word in resend_key.lower() for word in _PLACEHOLDERS):
        raise ContractError('V3__Auth__ResendApiKey: protected Resend key is missing or malformed.')
    code_key = _secret_bytes('V3__Auth__CodeHashKey', values['V3__Auth__CodeHashKey'])
    pin_key = _secret_bytes('V3__Auth__PinPepper', values['V3__Auth__PinPepper'])
    if hmac.compare_digest(code_key, pin_key):
        raise ContractError('V3__Auth__CodeHashKey and V3__Auth__PinPepper must be independent.')
    certificate_password = values['V3__Auth__CookieCertificatePassword']
    if len(certificate_password) < 16 or any(word in certificate_password.lower() for word in _PLACEHOLDERS):
        raise ContractError('V3__Auth__CookieCertificatePassword: protected password is missing or unsafe.')


def assemble(base_path: pathlib.Path, auth_path: pathlib.Path) -> dict[str, str]:
    base = parse_env(base_path, BASE_KEYS, 'base operational configuration')
    auth = parse_env(auth_path, AUTH_KEYS, 'authentication configuration')
    overlap = set(base) & set(auth)
    if overlap - {'V3__Auth__FirebaseProjectId'}:
        raise ContractError(f'Duplicate key across inputs: {sorted(overlap)[0]}.')
    if 'V3__Auth__FirebaseProjectId' not in overlap:
        raise ContractError('V3__Auth__FirebaseProjectId must be independently present in both inputs.')
    if base['V3__Auth__FirebaseProjectId'] != auth['V3__Auth__FirebaseProjectId']:
        raise ContractError('V3__Auth__FirebaseProjectId differs between operational and authentication inputs.')
    complete = {**base, **auth}
    validate_complete(complete)
    return complete


def write_atomic(output: pathlib.Path, values: dict[str, str], owner_uid: int = 0, owner_gid: int = 0) -> None:
    parent = output.parent
    try:
        parent_details = parent.lstat()
    except OSError as error:
        raise ContractError('Output directory is unavailable.') from error
    if (not stat.S_ISDIR(parent_details.st_mode) or parent.is_symlink()
            or parent_details.st_uid != owner_uid or stat.S_IMODE(parent_details.st_mode) & 0o022):
        raise ContractError('Output directory must be a protected owner-controlled real directory.')
    if output.exists() or output.is_symlink():
        details = output.lstat()
        if (not stat.S_ISREG(details.st_mode) or output.is_symlink()
                or details.st_uid != owner_uid or details.st_gid != owner_gid
                or stat.S_IMODE(details.st_mode) != 0o600):
            raise ContractError('Output path must be an owner-controlled mode-0600 regular non-symlink file.')
    payload = ''.join(f'{key}={values[key]}\n' for key in OUTPUT_KEYS).encode('utf-8')
    descriptor = -1
    temporary: pathlib.Path | None = None
    try:
        descriptor, name = tempfile.mkstemp(prefix='.api.env.', dir=parent)
        temporary = pathlib.Path(name)
        os.fchmod(descriptor, 0o600)
        details = os.fstat(descriptor)
        if details.st_uid != owner_uid or details.st_gid != owner_gid:
            os.fchown(descriptor, owner_uid, owner_gid)
        with os.fdopen(descriptor, 'wb', closefd=True) as stream:
            descriptor = -1
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, output)
        directory_fd = os.open(parent, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(directory_fd)
        finally:
            os.close(directory_fd)
    except OSError as error:
        raise ContractError('Secure atomic output write failed.') from error
    finally:
        if descriptor >= 0:
            os.close(descriptor)
        if temporary is not None and temporary.exists():
            temporary.unlink()


def _metadata(path: pathlib.Path, kind: str, uid: int, gid: int, mode: int) -> list[str]:
    try:
        details = path.lstat()
    except OSError:
        return [f'{path}: missing or inaccessible metadata.']
    errors: list[str] = []
    expected_kind = stat.S_ISREG if kind == 'file' else stat.S_ISDIR
    if path.is_symlink() or not expected_kind(details.st_mode):
        errors.append(f'{path}: must be a non-symlink {kind}.')
    if details.st_uid != uid or details.st_gid != gid:
        errors.append(f'{path}: owner must be UID/GID {uid}:{gid}.')
    if stat.S_IMODE(details.st_mode) != mode:
        errors.append(f'{path}: mode must be {mode:04o}.')
    return errors


def validate_host_metadata(firebase: pathlib.Path, certificate: pathlib.Path,
                           keys: pathlib.Path, uid: int = 1654, gid: int = 1654) -> list[str]:
    errors = _metadata(firebase, 'file', uid, gid, 0o400)
    errors += _metadata(certificate, 'file', uid, gid, 0o400)
    errors += _metadata(keys, 'directory', uid, gid, 0o700)
    return errors


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description='Weymela V3 Pilot runtime preparation; no deployment actions.')
    commands = parser.add_subparsers(dest='command', required=True)
    assembly = commands.add_parser('assemble-api-env')
    assembly.add_argument('--base', required=True, type=pathlib.Path)
    assembly.add_argument('--auth', required=True, type=pathlib.Path)
    assembly.add_argument('--output', required=True, type=pathlib.Path)
    assembly.add_argument('--check-only', action='store_true')
    metadata = commands.add_parser('validate-host-metadata')
    metadata.add_argument('--firebase-admin', type=pathlib.Path, default=FIREBASE_ADMIN_HOST)
    metadata.add_argument('--cookie-certificate', type=pathlib.Path, default=COOKIE_CERTIFICATE_HOST)
    metadata.add_argument('--cookie-keys', type=pathlib.Path, default=COOKIE_KEYS_HOST)
    metadata.add_argument('--expected-uid', type=int, default=1654)
    metadata.add_argument('--expected-gid', type=int, default=1654)
    return parser


def main(argv: list[str] | None = None) -> int:
    arguments = _parser().parse_args(argv)
    try:
        if arguments.command == 'assemble-api-env':
            values = assemble(arguments.base, arguments.auth)
            if not arguments.check_only:
                if os.geteuid() != 0:
                    raise ContractError('Root execution is required for the protected output write.')
                write_atomic(arguments.output, values)
            print(json.dumps({
                'status': 'valid' if arguments.check_only else 'assembled',
                'keys': list(OUTPUT_KEYS),
                'secretsRedacted': sorted(SECRET_KEYS),
            }, indent=2))
            return 0
        errors = validate_host_metadata(arguments.firebase_admin, arguments.cookie_certificate,
                                        arguments.cookie_keys, arguments.expected_uid,
                                        arguments.expected_gid)
        print(json.dumps({'status': 'valid' if not errors else 'invalid', 'errors': errors}, indent=2))
        return 1 if errors else 0
    except ContractError as error:
        print(json.dumps({'status': 'invalid', 'error': str(error)}, indent=2))
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
