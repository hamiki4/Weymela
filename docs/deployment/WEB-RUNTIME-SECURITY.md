# Web runtime libuuid security correction

## Confirmed cause and upstream check

The owner's sanitized release #4 report identifies Alpine 3.24.1 `libuuid 2.42.1-r0`: **7 HIGH, 0 CRITICAL vulnerabilities; 0 HIGH/CRITICAL secrets**. This is an nginx runtime OS package, not React/npm.

| Finding | Alpine fixed version |
|---|---|
| CVE-2026-53612 | 2.42.3-r0 |
| CVE-2026-53613 | 2.42.3-r0 |
| CVE-2026-53614 | 2.42.3-r0 |
| CVE-2026-76642 | 2.42.3-r0 |
| CVE-2026-78408 | 2.42.3-r1 |
| CVE-2026-78409 | 2.42.3-r0 |
| CVE-2026-78410 | 2.42.3-r0 |

Independently checked on 2026-09-12 against [Alpine's v3.24 security database](https://secdb.alpinelinux.org/v3.24/main.json), package origin `util-linux`, and the [x86_64 libuuid package](https://pkgs.alpinelinux.org/package/v3.24/main/x86_64/libuuid).

Read-only `docker buildx imagetools inspect nginx:stable-alpine --format '{{json .Manifest}}'` still resolves the **same** vulnerable upstream index as release #4:

```text
nginx:stable-alpine@sha256:dc5069ad14f19660b141b21236140b91656bf89bbc3e2417c70ae650cd66104c
linux/amd64 manifest: sha256:862dc06c359bfe5d3211e4106269f040d261e269e58ebf17060d8328c45067c0
nginx version: 1.30.4-alpine; amd64 image created: 2026-09-02T21:07:21Z
```

There is no newer patched `stable-alpine` digest to advance to at this check. No image layers were pulled, built or run on the application server.

## Small deterministic correction

- `tools/ci/build-image.sh` explicitly locks the above nginx digest, instead of resolving a moving runtime tag. Node and API/Worker base handling are unchanged.
- `docker/Dockerfile.web` fetches only the official, signed Alpine v3.24 x86_64 `libuuid-2.42.3-r1.apk` and verifies SHA-256 **8306e5bb577696c9069fe1dfd9e1dcc39d2d481c6a1b0e707fd03c3e21aa6aa2** before installation.
- `apk add --no-cache --no-network` installs this single local package using normal Alpine signature and dependency checks. No `--allow-untrusted`, global `apk upgrade`, repository refresh or unrelated upgrade. The inspected APK contains only libuuid library files and depends on the base's existing musl library. Failure to download, match checksum, verify signature or satisfy dependencies blocks the build.
- The Docker layer requires installed `libuuid=2.42.3-r1`. A separate restricted, networkless runtime inspection on the hosted builder records `web-runtime-packages.txt` and requires that exact installed package before emitting successful build outputs. Non-root nginx, camera policy and Web configuration are unchanged.
- The upstream base digest is unchanged; the derived Web image digest will be new and is recorded only after the successful build/scan/push. No derived image digest or successful scan is claimed here.

The exact package version and checksum deliberately fail closed if upstream removes/replaces the APK. Review a newer package/base deliberately; do not fall back to an unpinned install or suppress advisories.

## Mandatory validation still required on an authorized builder

Local checks exercise the CI guards using synthetic executables, not Docker builds. The application server has no configured off-server builder. Actual Web production build, runtime package verification and post-fix Trivy results therefore remain **PENDING** until an authorized off-server builder or GitHub-hosted run tests this exact patch.

Local regression evidence: **77 CI/security tests + 24 preparation tests = 101 passed, 0 failed**, including 17 new focused Web runtime-pin tests. Shell syntax and actionlint checks pass. The prior private-attestation, sanitized-failure-diagnostic and fail-closed manifest tests remain included. These results do not substitute for an actual patched-image scan.

On that builder only (Node 24, Docker Buildx and Trivy **v0.74.0** available), from the reviewed checkout:

```bash
set -euo pipefail
python3 -m unittest discover -s tests/ci -v
python3 -m unittest discover -s tests/preparation -v
npm --prefix src/Weymela.Web ci --no-audit --no-fund
npm --prefix src/Weymela.Web run build

# Inspection only: no publication, deployment or application-server execution.
mkdir -p .artifacts/release
node_digest="$(docker buildx imagetools inspect node:24-bookworm-slim --format '{{json .Manifest}}' | python3 -c 'import json,sys; print(json.load(sys.stdin)["digest"])')"
[[ "$node_digest" =~ ^sha256:[a-f0-9]{64}$ ]]
web_check_image="weymela-v3-web-security:$(git rev-parse HEAD)"
docker buildx build --platform linux/amd64 --load --provenance=false \
  --file docker/Dockerfile.web --tag "$web_check_image" \
  --build-arg "NODE_IMAGE=node:24-bookworm-slim@$node_digest" \
  --build-arg 'WEB_IMAGE=nginx:stable-alpine@sha256:dc5069ad14f19660b141b21236140b91656bf89bbc3e2417c70ae650cd66104c' .
docker run --rm --network none --read-only --cap-drop ALL \
  --security-opt no-new-privileges --entrypoint /sbin/apk "$web_check_image" info -v \
  > .artifacts/release/web-runtime-packages.txt
grep -Fx 'libuuid-2.42.3-r1' .artifacts/release/web-runtime-packages.txt

# Match the release policy exactly. Retain only sanitized diagnostics on failure.
if trivy image --scanners vuln,secret --severity HIGH,CRITICAL \
  --ignore-unfixed=false --exit-code 1 --format json \
  --output .artifacts/release/web-security.json "$web_check_image"; then
  echo 'Mandatory image scan passed; review the exact image/report before approval.'
else
  scan_status=$?
  COMPONENT=web BUILD_OUTCOME=success SCAN_OUTCOME=failure \
    PUBLISH_OUTCOME=skipped ATTESTATION_OUTCOME=skipped \
    python3 tools/ci/release-evidence.py diagnostics
  exit "$scan_status"
fi
```

Inspect the resulting report for all seven CVE IDs above and **all** HIGH/CRITICAL vulnerabilities/secrets, not only libuuid. Never upload raw secret findings. The hosted release workflow still uses the unchanged `vuln,secret / HIGH,CRITICAL / ignore-unfixed=false / exit-code=1` gate; it creates the same sanitized failure diagnostics. Private-user attestation stays `unavailable`, never falsely `attested`. Manifest still requires API, Worker, Web, all validation and migrations to succeed. This patch does not authorize a push, image publication or deployment.
