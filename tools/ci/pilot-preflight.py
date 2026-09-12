#!/usr/bin/env python3
"""Read-only Compose + release-manifest verification; never pulls or starts services."""
import argparse
import json
import pathlib
import re
import subprocess

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
        if part in images and (images[part]['digest'] != service['image'] or images[part]['commit'] != manifest.get('commit')):
            errors.append(f'{part}: source/digest does not match manifest.')
        if part != 'web' and service.get('ports'): errors.append(f'{part}: host port forbidden.')
        if part == 'web' and any(p.get('host_ip') != '127.0.0.1' or str(p.get('published')) != '18080' for p in service.get('ports', [])):
            errors.append('Web must use only approved loopback port18080.')
        if part in ('api', 'worker'):
            environment = service.get('environment', {})
            if environment.get('V3__FinancialWritesEnabled') != 'false' or environment.get('V3__EnableDevelopmentIdentity') != 'false':
                errors.append(f'{part}: preparation must stay frozen with Development identity disabled.')
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
