#!/usr/bin/env python3
import hashlib
import json
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

root = Path(__file__).resolve().parent
bundle, tool = map(Path, sys.argv[1:3])
plan = json.loads((root / 'release-plan.json').read_text())
manifest = subprocess.check_output(['java', '-jar', str(tool), 'dump', 'manifest', '--bundle', str(bundle)])
tree = ET.fromstring(manifest)
android = '{http://schemas.android.com/apk/res/android}'
assert tree.get('package') == plan['applicationId']
assert int(tree.get(android + 'versionCode')) == plan['versionCode']
assert tree.get(android + 'versionName') == plan['versionName']
assert int(tree.find('uses-sdk').get(android + 'targetSdkVersion')) >= 36
app = tree.find('application')
assert app.get(android + 'allowBackup') == 'false'
assert app.get(android + 'usesCleartextTraffic') == 'false'
with zipfile.ZipFile(bundle) as archive:
    names = archive.namelist()
    assert not any(name.upper().endswith(('.SF', '.RSA', '.DSA', '.EC')) for name in names), 'Validation bundle must be unsigned'
    assert not any(name.lower().endswith(('.jks', '.keystore', '.pfx', '.p12')) for name in names), 'Signing files must never be packaged'
    packaged = json.loads(archive.read('base/assets/public/android-release.json'))
    assert packaged['firebaseProjectId'] == 'weymela-production' and packaged['webImage'] == plan['webImage']
    assert archive.read('base/assets/public/v3-transport.js') == (root / 'v3-transport.js').read_bytes()
    cap = json.loads(archive.read('base/assets/capacitor.config.json'))
    assert cap['appId'] == plan['applicationId'] and 'url' not in cap.get('server', {})
    assert cap.get('loggingBehavior') == 'none' and cap['plugins']['CapacitorHttp']['enabled'] is False
    native_libraries = [name for name in names if name.endswith('.so')]
resources = subprocess.check_output(['java', '-jar', str(tool), 'dump', 'resources', '--bundle', str(bundle), '--values'])
assert b'weymela-production' in resources, 'Production Firebase native resources must be present'
report = {
    'applicationId': plan['applicationId'], 'versionCode': plan['versionCode'], 'versionName': plan['versionName'],
    'targetSdkVersion': int(tree.find('uses-sdk').get(android + 'targetSdkVersion')),
    'productionFirebaseNativeResources': True, 'productionWebImage': plan['webImage'],
    'bundleSha256': hashlib.sha256(bundle.read_bytes()).hexdigest(),
    'signed': False, 'validationCandidateOnly': True, 'nativeSharedLibraries': native_libraries,
    'liveBackendCompatibility': 'NOT VERIFIED; owner/live testing against ready V3 is required',
    'registeredUploadCertificate': plan.get('registeredUploadCertificateSha256', 'PENDING genuine Play Console evidence'), 'closedTestingSubmission': 'NOT PERFORMED'
}
bundle.with_suffix('.verification.json').write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report, indent=2))
