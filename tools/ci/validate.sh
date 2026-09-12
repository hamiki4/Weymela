#!/usr/bin/env bash
set -euo pipefail
# Runs on an isolated GitHub-hosted runner, not on the application server.
test "${GITHUB_ACTIONS:-}" = true || { printf '%s\n' 'Use an approved isolated external builder for full validation.' >&2; exit 1; }
mkdir -p .artifacts/ci
python3 tools/ci/repository-safety.py . --report .artifacts/ci/source-inventory.json
git diff --check
dotnet tool restore
dotnet restore Weymela.slnx -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=high -p:TreatWarningsAsErrors=true
dotnet build Weymela.slnx --configuration Release --no-restore --nologo
for suite in Domain.Tests Application.Tests Infrastructure.Tests Api.IntegrationTests; do
  dotnet test "tests/Weymela.$suite/Weymela.$suite.csproj" --configuration Release --no-build --no-restore --logger "trx;LogFileName=$suite.trx" --results-directory "$PWD/.artifacts/ci"
done
dotnet ef migrations has-pending-model-changes --project src/Weymela.Infrastructure --startup-project src/Weymela.Infrastructure --configuration Release --no-build
dotnet list Weymela.slnx package --vulnerable --include-transitive --format json --no-restore > .artifacts/ci/nuget-audit.json
python3 tools/ci/dependency-gate.py .artifacts/ci/nuget-audit.json
npm --prefix src/Weymela.Web ci --no-fund
npm --prefix src/Weymela.Web audit --audit-level=high
npm --prefix src/Weymela.Web test
npm --prefix src/Weymela.Web run build
V3_NUGET_PACKAGES="${NUGET_PACKAGES:-$HOME/.nuget/packages}" node tools/acceptance/dependency-inventory.mjs
bash tools/ci/browser-tests.sh
