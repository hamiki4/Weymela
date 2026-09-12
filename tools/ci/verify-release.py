#!/usr/bin/env python3
"""Verify a downloaded CI release without executing its contents or touching runtime."""
import argparse
import hashlib
import json
import pathlib
import re

def verify(root):
    root = root.resolve()
    manifest_file = root/'release-manifest.json'
    manifest = json.loads(manifest_file.read_text())
    if not re.fullmatch(r'[a-f0-9]{40}', manifest.get('commit', '')):
        raise ValueError('Invalid source commit')
    if manifest.get('platform') != 'linux/amd64' or manifest.get('deploymentAuthorized') is not False or manifest.get('financialWritesEnabled') is not False:
        raise ValueError('Unexpected platform/authorization/freeze state')
    images = manifest.get('images', [])
    if len(images) != 3 or {i['component'] for i in images} != {'api', 'worker', 'web'}:
        raise ValueError('Incomplete runtime release')
    for item in images:
        if item['commit'] != manifest['commit'] or not re.fullmatch(r'ghcr\.io/[a-z0-9_.-]+/weymela-v3-'+item['component']+r'@sha256:[a-f0-9]{64}', item['digest']):
            raise ValueError('Invalid image source/digest')
    if manifest.get('migrations', {}).get('commit') != manifest['commit']:
        raise ValueError('Migration source mismatch')
    checksums = manifest.get('checksums', {})
    required = {'migrations/efbundle', 'migrations/v3-forward.sql', 'migrations/migration-manifest.json'}
    required.update(f'images/{p}-image.json' for p in ('api', 'worker', 'web'))
    if not required.issubset(checksums): raise ValueError('Incomplete checksum inventory')
    for name, digest in checksums.items():
        path = root/name
        if pathlib.PurePosixPath(name).is_absolute() or '..' in pathlib.PurePosixPath(name).parts or path.is_symlink() or not path.resolve().is_relative_to(root):
            raise ValueError('Unsafe release path')
        if not re.fullmatch(r'[a-f0-9]{64}', digest) or hashlib.sha256(path.read_bytes()).hexdigest() != digest:
            raise ValueError('Artifact checksum mismatch')
    for item in images:
        if json.loads((root/f"images/{item['component']}-image.json").read_text()) != item:
            raise ValueError('Image metadata mismatch')
    if json.loads((root/'migrations/migration-manifest.json').read_text()) != manifest['migrations']:
        raise ValueError('Migration metadata mismatch')
    return {'commit': manifest['commit'], 'verifiedArtifacts': len(checksums), 'deploymentAuthorized': False}

if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('directory')
    args=parser.parse_args()
    print(json.dumps(verify(pathlib.Path(args.directory)), indent=2))
