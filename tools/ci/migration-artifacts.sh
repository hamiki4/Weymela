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
mkdir -p .artifacts/migrations/grants/baseline-21 .artifacts/migrations/grants/current
cp database/grants/baseline-21/v3-verify.sql .artifacts/migrations/grants/baseline-21/v3-verify.sql
cp database/grants/v3-{api,worker,migrator,migrator-defaults,backup,verify}.sql .artifacts/migrations/grants/current/
python3 - <<'PY'
import hashlib,json,pathlib,subprocess
root=pathlib.Path('.artifacts/migrations')
files=sorted(pathlib.Path('src/Weymela.Infrastructure/Persistence/Migrations').glob('[0-9]*.cs'))
ids=[x.stem for x in files if not x.name.endswith('.Designer.cs')]
assert ids==['20260911225904_InitialV3Schema','20260911233032_AddViewRewardsQrAndPayouts','20260912011149_AddOperationalSecurityAndNotifications','20260913045523_AddAuthenticationRecovery','20260913054814_AddRoleEnrollments','20260913062900_AddPhoneLoginAliases','20260914022116_AddDevicePinSessionFoundation','20260916042557_AddPasswordCredentials','20260916202055_AddCustomerProfiles','20260917020034_AddProductHandoffTransactions','20260917233008_AddBusinessLedPromotionAndUgc','20260918144832_AddUgcCustomerOffers','20260919120000_AddUgcCustomerDiscountLimit','20260922004528_AddBusinessProfileCoordinates','20260922161742_AddCreatorPromotionContentSubmissions','20260922184111_AddPromotionLiveDurationSnapshots','20260923025814_AddCashierPreauthorizationsAndBusinessOwnerCheckout','20260924034537_AddAdminAccountAuthorityFoundation','20260925010921_AddViewAsSupportSessions','20260925203153_AddPlatformPromotionalFunding','20260925212120_RetireSupportSessions','20260928213157_AlignDepositReviewAuthority','20260928230108_AddCreatorNumbers','20260929022846_BindUgcSaleAssignments'], 'Migration set changed: review required'
commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
baseline='7d537bb8a83ed2757a2149261b8dd7a449f51629'
expected_baseline=subprocess.check_output(['git','show',f'{baseline}:database/grants/v3-verify.sql'])
assert (root/'grants/baseline-21/v3-verify.sql').read_bytes()==expected_baseline, 'Baseline grant verifier changed'
artifact_paths=[root/'efbundle',root/'v3-forward.sql',root/'grants/baseline-21/v3-verify.sql']
artifact_paths += [root/f'grants/current/v3-{name}.sql'
                   for name in ('api','worker','migrator','backup','migrator-defaults','verify')]
metadata={'commit':commit,'database':'weymela_v3_pilot','migrationOrder':ids,
 'grantContracts':{
  'from':{'sourceCommit':baseline,'migrationCount':21,'verifier':'grants/baseline-21/v3-verify.sql'},
  'to':{'sourceCommit':commit,'migrationCount':24,'scripts':[f'grants/current/v3-{name}.sql' for name in ('api','worker','migrator','backup','migrator-defaults')], 'verifier':'grants/current/v3-verify.sql'}},
 'files':{p.relative_to(root).as_posix():hashlib.sha256(p.read_bytes()).hexdigest()
          for p in artifact_paths}}
(root/'migration-manifest.json').write_text(json.dumps(metadata,indent=2)+'\n')
PY
(cd .artifacts/migrations && sha256sum efbundle v3-forward.sql migration-manifest.json grants/baseline-21/v3-verify.sql grants/current/*.sql > SHA256SUMS)
