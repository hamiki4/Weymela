#!/usr/bin/env bash
set -euo pipefail
# One independently scheduled suite per hosted runner. No default serial mega-job.
test "${GITHUB_ACTIONS:-}" = true || { printf '%s\n' 'Use an approved isolated external builder for full validation.' >&2; exit 1; }
suite="${1:?Choose unit, postgres, http, frontend, dotnet-checks or browser-build}"
mkdir -p .artifacts/ci

run_stage() {
  local name="$1" started elapsed status
  shift
  started=$SECONDS
  printf '::group::%s | %s | started %s\n' "$suite" "$name" "$(date -u +%FT%TZ)"
  if "$@"; then status=0; else status=$?; fi
  elapsed=$((SECONDS - started))
  printf '::endgroup::\nSTAGE %s / %s: %ss, exit %s\n' "$suite" "$name" "$elapsed" "$status"
  printf '%s\t%s\t%s\t%s\n' "$suite" "$name" "$elapsed" "$status" >> .artifacts/ci/stage-timings.tsv
  return "$status"
}

restore_build() {
  local project="$1"
  run_stage "restore $project" dotnet restore "$project" -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=high -p:TreatWarningsAsErrors=true
  run_stage "build $project" dotnet build "$project" --configuration Release --no-restore --nologo
}

test_project() {
  local name="$1" project="tests/Weymela.$1/Weymela.$1.csproj"
  restore_build "$project"
  run_stage "test $name" dotnet test "$project" --configuration Release --no-build --no-restore --logger "trx;LogFileName=$name.trx" --results-directory "$PWD/.artifacts/ci"
}

case "$suite" in
  unit)
    test_project Domain.Tests
    test_project Application.Tests
    ;;
  postgres) test_project Infrastructure.Tests ;;
  http) test_project Api.IntegrationTests ;;
  frontend)
    run_stage 'npm ci' npm --prefix src/Weymela.Web ci --no-fund
    run_stage 'npm audit' npm --prefix src/Weymela.Web audit --audit-level=high
    run_stage 'Vitest (all tests)' npm --prefix src/Weymela.Web test
    run_stage 'frontend production build' npm --prefix src/Weymela.Web run build
    ;;
  dotnet-checks)
    run_stage 'EF tool restore' dotnet tool restore
    restore_build Weymela.slnx
    run_stage 'EF pending-model' dotnet ef migrations has-pending-model-changes --project src/Weymela.Infrastructure --startup-project src/Weymela.Infrastructure --configuration Release --no-build
    # Keep JSON separate from timing/group annotations and preserve the audit CLI exit code.
    nuget_audit() { dotnet list Weymela.slnx package --vulnerable --include-transitive --format json --no-restore > .artifacts/ci/nuget-audit.json; }
    run_stage 'NuGet vulnerability report' nuget_audit
    run_stage 'NuGet vulnerability gate' python3 tools/ci/dependency-gate.py .artifacts/ci/nuget-audit.json
    export V3_NUGET_PACKAGES="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
    run_stage 'license inventory' node tools/acceptance/dependency-inventory.mjs
    ;;
  browser-build)
    restore_build tests/Weymela.BrowserHost/Weymela.BrowserHost.csproj
    run_stage 'npm ci' npm --prefix src/Weymela.Web ci --no-fund
    run_stage 'frontend production build for browser' npm --prefix src/Weymela.Web run build
    ;;
  *) printf '%s\n' 'Unknown validation suite.' >&2; exit 2 ;;
esac
