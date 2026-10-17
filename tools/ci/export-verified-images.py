#!/usr/bin/env python3
"""Transport already passed registry images without rebuilding or deploying them."""
import base64,hashlib,io,json,os,pathlib,re,stat,subprocess,tarfile,urllib.parse,urllib.request,zipfile
root=pathlib.Path('.artifacts/image-transfer');root.mkdir(parents=True,exist_ok=False);release=root/'release';release.mkdir();repo='hamiki4/Weymela';runId=os.environ['SOURCE_RELEASE_RUN'];assert runId.isdigit()
def gh(path):return json.loads(subprocess.check_output(['gh','api','repos/'+repo+'/'+path],text=True))
run=gh('actions/runs/'+runId);assert run['status']=='completed' and run['conclusion']=='success' and run['head_branch'] in ['main','release/final-pilot-production','release/payout-receiving-phone','release/creator-go-live-delivery']
jobs=gh('actions/runs/'+runId+'/jobs')['jobs'];assert all(x['conclusion']=='success' for x in jobs)
assert {'acceptance','images (api)','images (worker)','images (web)','manifest'}.issubset({x['name'].split('/')[-1].strip() for x in jobs})
items=[x for x in gh('actions/runs/'+runId+'/artifacts')['artifacts'] if x['name']=='weymela-release-package' and not x['expired']];assert len(items)==1;artifact=items[0]
raw=subprocess.check_output(['gh','api','repos/'+repo+'/actions/artifacts/'+str(artifact['id'])+'/zip']);assert 'sha256:'+hashlib.sha256(raw).hexdigest()==artifact['digest']
with zipfile.ZipFile(io.BytesIO(raw)) as archive:
 for item in archive.infolist():
  p=pathlib.PurePosixPath(item.filename);assert not p.is_absolute() and '..' not in p.parts and not stat.S_ISLNK(item.external_attr>>16)
 archive.extractall(release)
subprocess.run(['python3','tools/ci/verify-release.py',str(release)],check=True)
manifest=json.loads((release/'release-manifest.json').read_text());assert manifest['commit']==run['head_sha']
layout=root/'oci';blobs=layout/'blobs/sha256';blobs.mkdir(parents=True)
class Redirect(urllib.request.HTTPRedirectHandler):
 def redirect_request(self,request,fp,code,msg,headers,newurl):
  parsed=urllib.parse.urlparse(newurl);assert parsed.scheme=='https' and not parsed.username and not parsed.password
  redirected=super().redirect_request(request,fp,code,msg,headers,newurl)
  if urllib.parse.urlparse(request.full_url).netloc!=parsed.netloc:redirected.remove_header('Authorization')
  return redirected
opener=urllib.request.build_opener(Redirect())
roots=[];records=[]
for image in manifest['images']:
 match=re.fullmatch(r'ghcr.io/(hamiki4/weymela-v3-(?:api|worker|web))@(sha256:[0-9a-f]{64})',image['digest']);assert match
 package,digest=match.groups();assert image['scan']['status']=='passed' and image['buildStatus']==image['publishStatus']=='passed'
 basic=base64.b64encode((os.environ['GITHUB_ACTOR']+':'+os.environ['GH_TOKEN']).encode()).decode()
 request=urllib.request.Request('https://ghcr.io/token?'+urllib.parse.urlencode({'service':'ghcr.io','scope':'repository:'+package+':pull'}),headers={'Authorization':'Basic '+basic})
 with opener.open(request,timeout=30) as response:token=json.load(response)['token']
 headers={'Authorization':'Bearer '+token,'Accept':'application/vnd.oci.image.index.v1+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.docker.distribution.manifest.v2+json'}
 configs=[]
 def fetch(digest,kind,size=None):
  assert re.fullmatch(r'sha256:[0-9a-f]{64}',digest);path=blobs/digest.split(':')[1]
  if not path.exists():
   request=urllib.request.Request('https://ghcr.io/v2/'+package+'/'+kind+'/'+digest,headers=headers)
   with opener.open(request,timeout=120) as response,path.open('xb') as target:
    while chunk:=response.read(1024*1024):target.write(chunk)
  assert 'sha256:'+hashlib.sha256(path.read_bytes()).hexdigest()==digest
  if size is not None:assert path.stat().st_size==size
  return path
 def walk(digest,size=None):
  path=fetch(digest,'manifests',size);value=json.loads(path.read_bytes())
  if 'manifests' in value:
   for item in value['manifests']:walk(item['digest'],item['size'])
  else:
   config=value['config'];data=json.loads(fetch(config['digest'],'blobs',config['size']).read_bytes())
   if data.get('os')=='linux' and data.get('architecture')=='amd64':
    assert data.get('config',{}).get('Labels',{}).get('org.opencontainers.image.revision')==manifest['commit'];configs.append(config['digest'])
   for item in value['layers']:fetch(item['digest'],'blobs',item['size'])
  return {'mediaType':value['mediaType'],'digest':digest,'size':path.stat().st_size}
 descriptor=walk(digest);assert configs
 descriptor['annotations']={'io.containerd.image.name':image['digest'],'org.opencontainers.image.ref.name':image['digest']};roots.append(descriptor)
 records.append({'component':image['component'],'registryReference':image['digest'],'configDigests':configs,'originalBuildImageId':image['imageId'],'commit':manifest['commit']})
(layout/'oci-layout').write_text('{"imageLayoutVersion":"1.0.0"}\n');(layout/'index.json').write_text(json.dumps({'schemaVersion':2,'mediaType':'application/vnd.oci.image.index.v1+json','manifests':roots})+'\n')
archive=root/'verified-images.oci.tar'
with tarfile.open(archive,'w') as tar:
 for path in sorted(layout.rglob('*')):
  if path.is_file():tar.add(path,arcname=path.relative_to(layout).as_posix(),recursive=False)
record={'sourceReleaseRun':int(runId),'sourceCommit':manifest['commit'],'releaseArtifactDigest':artifact['digest'],'archive':archive.name,'archiveSha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'images':records,'rebuildPerformed':False,'deploymentPerformed':False,'registryCredentialsExported':False}
(root/'transfer-manifest.json').write_text(json.dumps(record,indent=2)+'\n');print(json.dumps(record))
