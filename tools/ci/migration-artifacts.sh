#!/usr/bin/env bash
set -euo pipefail
test "${GITHUB_ACTIONS:-}" = true
mkdir -p .artifacts/migrations
dotnet tool restore
dotnet build src/Weymela.Infrastructure --configuration Release --nologo
dotnet ef migrations has-pending-model-changes --project src/Weymela.Infrastructure --startup-project src/Weymela.Infrastructure --configuration Release --no-build
# Design-time factory uses port 1. These generate files; nothing connects/applies/seeds.
dotnet ef migrations bundle --project src/Weymela.Infrastructure --startup-project src/Weymela.Infrastructure --configuration Release --self-contained --target-runtime linux-x64 --output .artifacts/migrations/efbundle
dotnet ef migrations script 0 --idempotent --project src/Weymela.Infrastructure --startup-project src/Weymela.Infrastructure --configuration Release --no-build --output .artifacts/migrations/v3-forward.sql
python3 - <<'PY'
import hashlib,json,pathlib,subprocess
root=pathlib.Path('.artifacts/migrations')
files=sorted(pathlib.Path('src/Weymela.Infrastructure/Persistence/Migrations').glob('[0-9]*.cs'))
ids=[x.stem for x in files if not x.name.endswith('.Designer.cs')]
assert ids==['20260911225904_InitialV3Schema','20260911233032_AddViewRewardsQrAndPayouts','20260912011149_AddOperationalSecurityAndNotifications','20260913045523_AddAuthenticationRecovery','20260913054814_AddRoleEnrollments','20260913062900_AddPhoneLoginAliases','20260914022116_AddDevicePinSessionFoundation'], 'Migration set changed: review required'
metadata={'commit':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),'database':'weymela_v3_pilot','migrationOrder':ids,'files':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in root.iterdir() if p.is_file()}}
(root/'migration-manifest.json').write_text(json.dumps(metadata,indent=2)+'\n')
PY
(cd .artifacts/migrations && sha256sum efbundle v3-forward.sql migration-manifest.json > SHA256SUMS)
