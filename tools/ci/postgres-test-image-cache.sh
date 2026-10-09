#!/usr/bin/env bash
set -euo pipefail

readonly upstream_image='postgres@sha256:aa90e97ee862e558111d34cfb8b2c4bec768c2b039fb791341686928560263b3'
readonly local_image='postgres:17-alpine'
readonly expected_image_id='sha256:79bd7c99e923138f136f8009d6bffa66e21e9d4fda5c0c561b00fc9c90cfe537'

if [[ $# -ne 2 ]]; then
  echo 'usage: postgres-test-image-cache.sh prepare|load CACHE_DIRECTORY' >&2
  exit 2
fi

readonly mode="$1"
readonly cache_dir="$2"
readonly image_archive="$cache_dir/postgres-17-alpine-linux-amd64.tar"

case "$mode" in
  prepare)
    mkdir -p -- "$cache_dir"
    docker pull "$upstream_image"
    docker image tag "$upstream_image" "$local_image"
    docker image save --output "$image_archive" "$local_image"
    (cd -- "$cache_dir" && sha256sum postgres-17-alpine-linux-amd64.tar > postgres-17-alpine-linux-amd64.tar.sha256)
    ;;
  load)
    [[ -f "$image_archive" && -f "$image_archive.sha256" ]] || {
      echo 'Cached PostgreSQL image archive or checksum is missing.' >&2
      exit 1
    }
    (cd -- "$cache_dir" && sha256sum --check postgres-17-alpine-linux-amd64.tar.sha256)
    docker image load --input "$image_archive"
    actual_image_id="$(docker image inspect --format '{{.Id}}' "$local_image")"
    [[ "$actual_image_id" == "$expected_image_id" ]] || {
      echo 'Cached PostgreSQL image ID does not match the pinned Docker Official Image.' >&2
      exit 1
    }
    ;;
  *)
    echo "unknown mode: $mode" >&2
    exit 2
    ;;
esac
