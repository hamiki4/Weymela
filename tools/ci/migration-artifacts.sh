#!/usr/bin/env bash
set -euo pipefail
test "${GITHUB_ACTIONS:-}" = true
mkdir -p .artifacts/migrations
dotnet tool restore
dotnet build src/Weymela.Infrastructure --configuration Release --nologo
dotnet ef migrations has-pending-model-changes --project src/Weymela.Infrastructure --startup-project src/Weymela.Infrastructure --configuration Release --no-build
# Design-time factory uses port 1. These generate files; nothing connects/applies/seeds.
dotnet ef migrations bundle --project src/Weymela.Infrastructure --startup-project src/Weymela.Infrastructure --configuration Release --self-contained --target-runtime linux-x64 --output .artifacts/migrations/efbundle --force
dotnet ef migrations script 0 --idempotent --project src/Weymela.Infrastructure --startup-project src/Weymela.Infrastructure --configuration Release --no-build --output .artifacts/migrations/v3-forward.sql
mkdir -p .artifacts/migrations/grants/baseline-28 .artifacts/migrations/grants/current
baseline_commit=0c546ee17030e1cf44f1e6d667a02611c6fc0ac2
git show "$baseline_commit:database/grants/v3-verify.sql" > .artifacts/migrations/grants/baseline-28/v3-verify.sql
printf '%s  %s\n' a5568f3a9f9a59b686b4c8b44df8bd3729256176cb2021324a54522991cfdf64 .artifacts/migrations/grants/baseline-28/v3-verify.sql | sha256sum --check --status
cp database/grants/v3-{api,worker,migrator,migrator-defaults,backup,verify}.sql .artifacts/migrations/grants/current/
python3 - <<'PY'
import hashlib,json,pathlib,re,subprocess
root=pathlib.Path('.artifacts/migrations')
files=sorted(pathlib.Path('src/Weymela.Infrastructure/Persistence/Migrations').glob('[0-9]*.cs'))
ids=[x.stem for x in files if not x.name.endswith('.Designer.cs')]
commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
baseline='0c546ee17030e1cf44f1e6d667a02611c6fc0ac2'
baseline_paths=subprocess.check_output(['git','ls-tree','-r','--name-only',baseline,'--',
    'src/Weymela.Infrastructure/Persistence/Migrations'],text=True).splitlines()
baseline_ids=[pathlib.PurePosixPath(p).stem for p in baseline_paths
              if re.fullmatch(r'[0-9]{14}_.+\.cs',pathlib.PurePosixPath(p).name)
              and not p.endswith('.Designer.cs')]
approved_upgrade=['20261006050542_CompleteCreatorCollaborationWorkflow']
assert len(baseline_ids)==28 and ids==baseline_ids+approved_upgrade, 'Migration set changed: review required'
expected_baseline=subprocess.check_output(['git','show',f'{baseline}:database/grants/v3-verify.sql'])
assert (root/'grants/baseline-28/v3-verify.sql').read_bytes()==expected_baseline, 'Baseline grant verifier changed'
artifact_paths=[root/'efbundle',root/'v3-forward.sql',root/'grants/baseline-28/v3-verify.sql']
artifact_paths += [root/f'grants/current/v3-{name}.sql'
                   for name in ('api','worker','migrator','backup','migrator-defaults','verify')]
metadata={'commit':commit,'database':'weymela_v3_pilot','migrationOrder':ids,
 'grantContracts':{
  'from':{'sourceCommit':baseline,'migrationCount':28,'verifier':'grants/baseline-28/v3-verify.sql'},
  'to':{'sourceCommit':commit,'migrationCount':len(ids),'scripts':[f'grants/current/v3-{name}.sql' for name in ('api','worker','migrator','backup','migrator-defaults')], 'verifier':'grants/current/v3-verify.sql'}},
 'files':{p.relative_to(root).as_posix():hashlib.sha256(p.read_bytes()).hexdigest()
          for p in artifact_paths}}
(root/'migration-manifest.json').write_text(json.dumps(metadata,indent=2)+'\n')
PY
(cd .artifacts/migrations && sha256sum efbundle v3-forward.sql migration-manifest.json grants/baseline-28/v3-verify.sql grants/current/*.sql > SHA256SUMS)
