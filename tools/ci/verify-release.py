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
    web = next(item for item in images if item['component'] == 'web')
    firebase_target = web.get('firebaseTarget')
    if firebase_target is None and web.get('firebaseProjectId') == 'weymela-pilot':
        firebase_target = 'pilot'  # Existing immutable Pilot release manifests predate target metadata.
    if (firebase_target not in ('pilot', 'production')
            or web.get('firebaseProjectId') != {
                'pilot': 'weymela-pilot', 'production': 'weymela-production'
            }[firebase_target]):
        raise ValueError('Invalid Web Firebase target/project identity')
    if manifest.get('migrations', {}).get('commit') != manifest['commit']:
        raise ValueError('Migration source mismatch')
    migrations = manifest['migrations']
    migration_target = migrations.get('target')
    if migration_target is None and firebase_target == 'pilot':
        migration_target = 'pilot'  # Legacy immutable Pilot bundles predate migration target metadata.
    if migration_target != firebase_target:
        raise ValueError('Migration target does not match Web Firebase target')
    baseline = 'grants/baseline-29/v3-verify.sql'
    current = [f'grants/current/v3-{name}.sql' for name in
               ('api', 'worker', 'migrator', 'backup', 'migrator-defaults')]
    verifier = 'grants/current/v3-verify.sql'
    contracts = migrations.get('grantContracts', {})
    expected_order = [
        '20260911225904_InitialV3Schema',
        '20260911233032_AddViewRewardsQrAndPayouts',
        '20260912011149_AddOperationalSecurityAndNotifications',
        '20260913045523_AddAuthenticationRecovery',
        '20260913054814_AddRoleEnrollments',
        '20260913062900_AddPhoneLoginAliases',
        '20260914022116_AddDevicePinSessionFoundation',
        '20260916042557_AddPasswordCredentials',
        '20260916202055_AddCustomerProfiles',
        '20260917020034_AddProductHandoffTransactions',
        '20260917233008_AddBusinessLedPromotionAndUgc',
        '20260918144832_AddUgcCustomerOffers',
        '20260919120000_AddUgcCustomerDiscountLimit',
        '20260922004528_AddBusinessProfileCoordinates',
        '20260922161742_AddCreatorPromotionContentSubmissions',
        '20260922184111_AddPromotionLiveDurationSnapshots',
        '20260923025814_AddCashierPreauthorizationsAndBusinessOwnerCheckout',
        '20260924034537_AddAdminAccountAuthorityFoundation',
        '20260925010921_AddViewAsSupportSessions',
        '20260925203153_AddPlatformPromotionalFunding',
        '20260925212120_RetireSupportSessions',
        '20260928213157_AlignDepositReviewAuthority',
        '20260928230108_AddCreatorNumbers',
        '20260929022846_BindUgcSaleAssignments',
        '20260929203557_AddUgcPlatformCapacities',
        '20260930031549_AddCreatorProfilePhotos',
        '20260930210000_AddAgreementDeadlines',
        '20261001043831_AddAdminVerifiedAudienceAndEnforcement',
        '20261006050542_CompleteCreatorCollaborationWorkflow',
        '20261007041919_AlignFinancialUatFlows',
    ]
    expected_count = len(expected_order)
    if (contracts.get('from') != {'sourceCommit':'1c552db44eb196603f56b623082b3cd629a46d91',
                                  'migrationCount':29, 'verifier':baseline}
            or contracts.get('to') != {'sourceCommit':manifest['commit'],
                                       'migrationCount':expected_count, 'scripts':current, 'verifier':verifier}
            or migrations.get('migrationOrder') != expected_order):
        raise ValueError('Grant contract stage/source mismatch')
    checksums = manifest.get('checksums', {})
    required = {'migrations/efbundle', 'migrations/v3-forward.sql', 'migrations/migration-manifest.json'}
    required.update('migrations/'+name for name in [baseline, *current, verifier])
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
    migration_files = migrations.get('files', {})
    artifact_names = ['efbundle', 'v3-forward.sql', baseline, *current, verifier]
    if set(migration_files) != set(artifact_names):
        raise ValueError('Unexpected migration/grant artifact inventory')
    for name in artifact_names:
        if migration_files.get(name) != checksums['migrations/'+name]:
            raise ValueError('Grant/migration checksum binding mismatch')
    if migration_files[baseline] != 'd2d62970fe2e648ddd314d9a0245b23f0a87590357e1abfb78013dcc905e6366':
        raise ValueError('Baseline grant verifier digest mismatch')
    return {'commit': manifest['commit'], 'verifiedArtifacts': len(checksums), 'deploymentAuthorized': False}

if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('directory')
    args=parser.parse_args()
    print(json.dumps(verify(pathlib.Path(args.directory)), indent=2))
