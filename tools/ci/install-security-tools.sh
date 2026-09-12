#!/usr/bin/env bash
set -euo pipefail
# Official release archives; fixed checksums, no curl | sh or floating executable.
tool_dir="${1:?Provide an isolated tool directory}"
mkdir -p "$tool_dir"
curl --fail --silent --show-error --location https://github.com/rhysd/actionlint/releases/download/v1.7.12/actionlint_1.7.12_linux_amd64.tar.gz -o "$tool_dir/actionlint.tar.gz"
printf '%s  %s\n' 8aca8db96f1b94770f1b0d72b6dddcb1ebb8123cb3712530b08cc387b349a3d8 "$tool_dir/actionlint.tar.gz" | sha256sum --check --status
tar -xzf "$tool_dir/actionlint.tar.gz" -C "$tool_dir" actionlint
curl --fail --silent --show-error --location https://github.com/gitleaks/gitleaks/releases/download/v8.30.1/gitleaks_8.30.1_linux_x64.tar.gz -o "$tool_dir/gitleaks.tar.gz"
printf '%s  %s\n' 551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb "$tool_dir/gitleaks.tar.gz" | sha256sum --check --status
tar -xzf "$tool_dir/gitleaks.tar.gz" -C "$tool_dir" gitleaks
