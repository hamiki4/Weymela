#!/usr/bin/env bash
set -euo pipefail
test "${GITHUB_ACTIONS:-}" = true
export V3_SOURCE_ROOT="$PWD"
export V3_TEST_BUILD_REVISION="${GITHUB_SHA:-local}"
mkdir -p .artifacts/ci
# Browser installation is a separate timed workflow step. This supervisor starts the
# already-built DLL directly and always bounds shutdown of its own process group.
exec python3 tools/ci/browser-host.py
