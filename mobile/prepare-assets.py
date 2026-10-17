#!/usr/bin/env python3
"""Copy the pinned, tested Production Web image; do not rebuild Web or backend."""
import json
import shutil
import subprocess
from pathlib import Path

root = Path(__file__).resolve().parent
plan = json.loads((root / 'release-plan.json').read_text())
metadata = json.loads(subprocess.check_output(['docker', 'image', 'inspect', plan['webImage']]))[0]
assert metadata['Config']['Labels']['org.opencontainers.image.revision'] == plan['webSourceCommit']
name = 'weymela-android-web-static-copy'
subprocess.run(['docker', 'create', '--name', name, '--network', 'none', plan['webImage']], check=True, stdout=subprocess.DEVNULL)
try:
    dist = root / 'dist'
    if dist.exists(): shutil.rmtree(dist)
    subprocess.run(['docker', 'cp', name + ':/usr/share/nginx/html', str(dist)], check=True)
finally:
    subprocess.run(['docker', 'rm', name], check=True, stdout=subprocess.DEVNULL)
project = dist / 'firebase-project.txt'
target = dist / 'firebase-target.txt'
if project.exists(): assert project.read_text().strip() == 'weymela-production'
if target.exists(): assert target.read_text().strip() == 'production'
html = (dist / 'index.html').read_text()
assert '/api/v1/' not in html and 'id="root"' in html
html = html.replace('<head>', '<head>\n<meta http-equiv="Content-Security-Policy" content="default-src \'self\'; script-src \'self\'; style-src \'self\' \'unsafe-inline\'; img-src \'self\' data: blob:; media-src \'self\' blob:; connect-src \'self\' https://identitytoolkit.googleapis.com https://securetoken.googleapis.com; frame-src https://www.youtube-nocookie.com; base-uri \'self\'; form-action \'self\'">\n<script src="/v3-transport.js"></script>\n<script src="/v3-media.js"></script>', 1)
(dist / 'index.html').write_text(html)
shutil.copyfile(root / 'v3-transport.js', dist / 'v3-transport.js')
shutil.copyfile(root / 'v3-media.js', dist / 'v3-media.js')
(dist / 'android-release.json').write_text(json.dumps({k: plan[k] for k in ['applicationId', 'versionCode', 'webSourceCommit', 'webImage', 'firebaseProjectId', 'apiOrigin']}))
print('Pinned Production V3 Web assets prepared for the existing Android shell.')
