#!/usr/bin/env bash
set -euo pipefail
test "${GITHUB_ACTIONS:-}" = true || { printf '%s\n' 'Image builds are restricted to CI/external builders.' >&2; exit 1; }
component="${1:?api, worker or web required}"
case "$component" in api|worker|web) ;; *) exit 1;; esac
test "${GITHUB_REF:-}" = refs/heads/main
test "${GITHUB_SHA:-}" = "$(git rev-parse HEAD)"
owner="$(printf '%s' "$GITHUB_REPOSITORY_OWNER" | tr '[:upper:]' '[:lower:]')"
release="v3-${GITHUB_SHA}-run${GITHUB_RUN_ID}-attempt${GITHUB_RUN_ATTEMPT}"
image="ghcr.io/$owner/weymela-v3-$component:$release"
mkdir -p .artifacts/release
if docker buildx imagetools inspect "$image" >/dev/null 2>&1; then
  printf '%s\n' 'Refusing to overwrite an existing release tag.' >&2; exit 1
fi
resolve_base() {
  local reference="$1" digest
  digest="$(docker buildx imagetools inspect "$reference" --format '{{json .Manifest}}' | python3 -c 'import json,sys; print(json.load(sys.stdin)["digest"])')"
  [[ "$digest" =~ ^sha256:[a-f0-9]{64}$ ]] || exit 1
  printf '%s@%s' "$reference" "$digest"
}
if test "$component" = web; then
  node_image="$(resolve_base node:24-bookworm-slim)"
  # Current upstream still contains vulnerable libuuid. Keep the Alpine base
  # fixed while Dockerfile.web applies the checksum-pinned, package-only fix.
  web_image="nginx:stable-alpine@sha256:dc5069ad14f19660b141b21236140b91656bf89bbc3e2417c70ae650cd66104c"
  build_args=(--build-arg "NODE_IMAGE=$node_image" --build-arg "WEB_IMAGE=$web_image")
  printf '%s\n%s\n' "$node_image" "$web_image" > ".artifacts/release/$component-bases.txt"
else
  sdk_image="$(resolve_base mcr.microsoft.com/dotnet/sdk:10.0-noble)"
  runtime_image="$(resolve_base mcr.microsoft.com/dotnet/aspnet:10.0-noble)"
  build_args=(--build-arg "SDK_IMAGE=$sdk_image" --build-arg "RUNTIME_IMAGE=$runtime_image")
  printf '%s\n%s\n' "$sdk_image" "$runtime_image" > ".artifacts/release/$component-bases.txt"
fi
docker buildx build --platform linux/amd64 --load --provenance=false \
  --file "docker/Dockerfile.$component" --tag "$image" \
  --label "org.opencontainers.image.source=https://github.com/$GITHUB_REPOSITORY" \
  --label "org.opencontainers.image.revision=$GITHUB_SHA" \
  --label "org.opencontainers.image.version=$release" "${build_args[@]}" .
if test "$component" = web; then
  # Check the actual built runtime on the hosted builder, before Trivy/publish.
  docker run --rm --network none --read-only --cap-drop ALL \
    --security-opt no-new-privileges --entrypoint /sbin/apk "$image" info -v \
    > .artifacts/release/web-runtime-packages.txt
  grep -Fx 'libuuid-2.42.3-r1' .artifacts/release/web-runtime-packages.txt
fi
printf 'image=%s\nrelease=%s\n' "$image" "$release" >> "$GITHUB_OUTPUT"
