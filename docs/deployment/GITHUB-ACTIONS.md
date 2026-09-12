# V3 GitHub Actions / registry preparation

Implemented as source in Phase7. No GitHub repository/remote, commit, push, secret, workflow run or registry package was created. Workflows are validated locally for syntax and safety, not claimed remotely green.

## Workflows

After the first hosted45minute timeout,validation is split into seven independent jobs plus an explicit fail-closed acceptance gate. See [CI timeout correction](CI-TIMEOUT-CORRECTION.md) for evidence limits,job/step timeouts,bounded BrowserHost cleanup,targeted tests and exact review-branch commit/push commands. Existing Phase7 preparation evidence below is historical;remote execution of the corrected graph remains pending.

| Workflow | Trigger / permissions | Result |
|---|---|---|
| `ci.yml` | PR,manual,or reusable workflow call; `contents:read`,no deployment secrets | Secret + workflow scan,repository safety/format gate,.NET restore/Release build,Domain/Application/PostgreSQL Testcontainers/HTTP API tests,EF pending-model,NuGet/npm audits,frontend tests/production build,real BrowserHost/Playwright suite |
| `release.yml` | main push or manual on main; same validation first | Hosted linux/amd64 API/Worker/Web builds,security scan before push,GHCR digest+size records,SBOM,OIDC provenance,offline migration bundle/SQL/checksums,complete release manifest |
| `pilot-approval.yml` | manual on main,protected `v3-pilot` environment | Verifies successful main release run/source repository,downloads/checks release manifest; **review only, no server connection/deployment/unfreeze** |

Hosted Ubuntu24.04 runners only, .NET10 SDK, Node24, Testcontainers PostgreSQL17; no application-server self-hosted runner. Repository EF tool manifest pins10.0.0 to current EF dependencies. PR has no package-write/OIDC permissions, no secrets inheritance and no `pull_request_target`. Full commit-pinned actions are resolved from official upstream tags. Gitleaks/actionlint binary downloads have fixed checksums. Dependency gates fail on high/critical (including unfixed),scanner errors fail closed; review advisories rather than masking with global ignores.

The format gate checks all candidate text for LF/trailing whitespace plus `git diff --check`; it intentionally does not mass-reformat the accepted implementation. No claim that a full `dotnet format` rewrite has occurred. `dotnet list ... --format json` output is parsed because vulnerable packages alone do not always cause a nonzero CLI exit. NuGet restore errors/audit failures and npm audit failures block. Runtime-image scan includes OS/library vulnerabilities and secrets, producing JSON; SBOM comes from the same scanned image. License inventory is generated for owner review, not legal approval.

Browser tests own a disposable randomly named PostgreSQL container and loopback server on the hosted runner; never use external DB secrets. No broad DB connection env reaches PRs. Only safe test results/inventory are uploaded; never `.artifacts/browser-host.json`,a directory-wide upload of acceptance artifacts,raw session keys,QR tokens or test-host logs. CI artifact retention14days,release30days; archive accepted release manifests/migration artifacts off-host before expiry.

## Owner setup BEFORE enabling publication

1. Select private repository owner/name and approve initial commit/push after [source safety](V3-REPOSITORY-INVENTORY.md). No existing V2 repository changes.
2. Protect main: PR review,required `V3 validation / acceptance` (confirm actual check name on first run),no force push/deletion,restrict admin bypass as policy permits. Require review of workflows,finance,auth and migrations. No `CODEOWNERS` placeholder account that cannot approve; owner configures real team/user.
3. Create GitHub environments `v3-registry` and `v3-pilot`,required reviewers,prevent self-review,restrict to protected main. Verify the account/plan supports environment review and private-repository attestations. An environment name in YAML **does not itself establish a protection rule**. If unsupported, stop and approve an equivalent protected attestation/release process; do not silently drop the gate.
4. After protections are verified set repository variable `V3_REGISTRY_PUBLISH_ENABLED=true`; default missing value prevents all image build/publish jobs. Set `V3_PILOT_REVIEW_ENABLED=true` only after the manual review process exists. Neither variable deploys anything.
5. Allow GHCR package creation for this repository; link packages to source via OCI source label. Build job alone gets `packages:write`, `id-token:write`, `attestations:write` and `contents:read`. It authenticates with ephemeral `GITHUB_TOKEN`; no registry password/PAT in source or build args. Test and migration jobs need no package write or live secrets.
6. A later server pull account gets **read:packages only** for the three private V3 packages (with organization SSO authorization where required), stored in protected Docker credential storage,never Compose/source. No SSH deploy key is required by these workflows.

GHCR fits GitHub-owned source/workflow permission boundaries. Refer to official [GHCR authentication/package linkage](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry) and [GitHub image publication/provenance](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images). Configure [environment protection rules](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments) explicitly; no automatic Production deployment exists.

## Build/release identities

Images: `ghcr.io/<lowercase-owner>/weymela-v3-api`, `...-worker`, `...-web`. Release tag `v3-<full-commit>-run<run-id>-attempt<attempt>` prevents re-run overwrite; build refuses existing tag. Pin runtime consumption to the **digest**, since registry tags alone are mutable administratively. No `latest` tag is published. Protect deletion of accepted packages; keep current and previous schema-compatible image sets.

Each build resolves official .NET10 SDK/ASP.NET Ubuntu,node24 and nginx stable base tags to digests before build and records them. Runtime images contain publish/static output,not SDK/source/tests/node_modules. Worker needs ASP.NET shared framework due to its existing FrameworkReference. API contains curl for bounded health check. No image build/pull was performed here. Actual sizes,base security posture and first CI results remain acceptance gates, not estimates labeled PASS.

Images are loaded on CI for pre-push Trivy scanning, then the same local image is pushed; GitHub OIDC attests its exact registry digest. BuildKit embedded provenance is disabled for `--load`; GitHub provenance and recorded base/source digests are the release evidence,not an assertion of full SLSA materials completeness. SBOM artifacts are checksummed in the release package. Example later verification: `gh attestation verify oci://ghcr.io/OWNER/weymela-v3-api@sha256:DIGEST --repo OWNER/REPOSITORY`; use exact identities from the manifest.

The release package is assembled only after all three images and migration job succeed. Partial registry publication does not constitute a release. It includes source commit,run ID,platform,image digests/IDs/sizes,SBOM/security/base metadata,migration order/checksums and `deploymentAuthorized=false`. Releasing/tagging images is not permission to migrate/unfreeze. Owner archives approved evidence and verifies capacity,backup/legal/adapter gates before a separate V3-only deployment authorization.

## Local read-only validation commands

```sh
python3 tools/ci/repository-safety.py . --report .artifacts/phase7-repository-inventory.json
python3 -m unittest discover -s tests/preparation -v
git diff --check
# Use the checksum-pinned tools installed into an isolated /tmp directory:
/approved/tool-directory/actionlint -shellcheck=''
/approved/tool-directory/gitleaks dir . --redact --no-banner --exit-code 1
```

Before an authorized runtime operation, `python3 tools/ci/pilot-preflight.py --env-file /etc/weymela-v3/pilot/compose.env --manifest /approved/v3-release/release-manifest.json` validates expanded Compose privately and prints only safe findings. It rejects mutable/V2 images,mismatched source/digests,public listeners,external networks and financial unfreeze. It does not pull,start,connect to a DB or authorize deployment. Always separately check capacity,ports,subnets,certificate access,role grants and complete release signatures/checksums.
