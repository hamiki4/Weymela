#!/usr/bin/env bash
set -euo pipefail
test "${GITHUB_ACTIONS:-}" = true
export V3_SOURCE_ROOT="$PWD"
(cd src/Weymela.Web && npx playwright install --with-deps chromium)
dotnet run --project tests/Weymela.BrowserHost --configuration Release --no-build > .artifacts/ci/browser-host.log 2>&1 &
browser_pid=$!
# Only this child process and its self-owned Testcontainers database are stopped.
trap 'kill -INT "$browser_pid" 2>/dev/null || true; wait "$browser_pid" || true' EXIT
for attempt in $(seq 1 90); do
  test -f .artifacts/browser-host.json && break
  kill -0 "$browser_pid" 2>/dev/null || { printf '%s\n' 'Isolated browser host failed.' >&2; exit 1; }
  sleep 1
done
test -f .artifacts/browser-host.json
(cd src/Weymela.Web && npm run e2e)
