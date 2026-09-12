# V3 GitHub Actions / registry preparation

Initially prepared as source in Phase7. The owner reports release #3 passed validation/migrations and published API/Worker, then failed on unsupported private-repository attestation. Web failed its pre-publication Trivy scan. The private-release correction below is validated locally; a complete hosted release remains pending. Exact release #3 logs, scan findings and registry digests were not accessible during this correction.

## Workflows

After the first hosted45minute timeout,validation is split into seven independent jobs plus an explicit fail-closed acceptance gate. See [CI timeout correction](CI-TIMEOUT-CORRECTION.md) for evidence limits,job/step timeouts,bounded BrowserHost cleanup,targeted tests and exact review-branch commit/push commands. The owner reports all seven validation jobs passed after that correction; initial Phase7 preparation evidence is historical.

| Workflow | Trigger / permissions | Result |
|---|---|---|
| `ci.yml` | PR,manual,or reusable workflow call; `contents:read`,no deployment secrets | Secret + workflow scan,repository safety/format gate,.NET restore/Release build,Domain/Application/PostgreSQL Testcontainers/HTTP API tests,EF pending-model,NuGet/npm audits,frontend tests/production build,real BrowserHost/Playwright suite |
| `release.yml` | main push or manual on main; same validation first | Hosted linux/amd64 API/Worker/Web builds,security scan before push,GHCR digest+size records,SBOM,optional supported OIDC provenance,offline migration bundle/SQL/checksums,complete release manifest |
| `pilot-approval.yml` | manual on main,protected `v3-pilot` environment | Verifies successful main release run/source repository,downloads/checks release manifest; **review only, no server connection/deployment/unfreeze** |

Hosted Ubuntu24.04 runners only, .NET10 SDK, Node24, Testcontainers PostgreSQL17; no application-server self-hosted runner. Repository EF tool manifest pins10.0.0 to current EF dependencies. PR has no package-write/OIDC permissions, no secrets inheritance and no `pull_request_target`. Full commit-pinned actions are resolved from official upstream tags. Gitleaks/actionlint binary downloads have fixed checksums. Dependency gates fail on high/critical (including unfixed),scanner errors fail closed; review advisories rather than masking with global ignores.

The format gate checks all candidate text for LF/trailing whitespace plus `git diff --check`; it intentionally does not mass-reformat the accepted implementation. No claim that a full `dotnet format` rewrite has occurred. `dotnet list ... --format json` output is parsed because vulnerable packages alone do not always cause a nonzero CLI exit. NuGet restore errors/audit failures and npm audit failures block. Runtime-image scan includes OS/library vulnerabilities and secrets, producing JSON; SBOM comes from the same scanned image. License inventory is generated for owner review, not legal approval.

Browser tests own a disposable randomly named PostgreSQL container and loopback server on the hosted runner; never use external DB secrets. No broad DB connection env reaches PRs. Only safe test results/inventory are uploaded; never `.artifacts/browser-host.json`,a directory-wide upload of acceptance artifacts,raw session keys,QR tokens or test-host logs. CI artifact retention14days,release30days; archive accepted release manifests/migration artifacts off-host before expiry.

## Owner setup BEFORE enabling publication

1. Select private repository owner/name and approve initial commit/push after [source safety](V3-REPOSITORY-INVENTORY.md). No existing V2 repository changes.
2. Protect main: PR review,required `V3 validation / acceptance` (confirm actual check name on first run),no force push/deletion,restrict admin bypass as policy permits. Require review of workflows,finance,auth and migrations. No `CODEOWNERS` placeholder account that cannot approve; owner configures real team/user.
3. Create GitHub environments `v3-registry` and `v3-pilot`,required reviewers,prevent self-review,restrict to protected main. Verify the account/plan supports environment review. An environment name in YAML **does not itself establish a protection rule**. If environment review is unsupported, stop and approve an equivalent protected release process; do not silently drop that gate. GitHub attestation availability is handled separately below and does not require making the repository public.
4. Image publication now follows successful main validation without a registry opt-in variable, tag, custom secret or dispatch input. Preserve `v3-registry` protections: if required reviewers are configured, image jobs wait for their approval. Set `V3_PILOT_REVIEW_ENABLED=true` only after the separate manual Pilot review process exists; it does not control image publication or deploy anything.
5. Allow GHCR package creation for this repository; link packages to source via OCI source label. Build job alone gets `packages:write` and `contents:read`; its existing `id-token:write` and `attestations:write` are retained only for the optional attestation action. It authenticates with ephemeral `GITHUB_TOKEN`; no registry password/PAT in source or build args. Test and migration jobs need no package write or live secrets.
6. A later server pull account gets **read:packages only** for the three private V3 packages (with organization SSO authorization where required), stored in protected Docker credential storage,never Compose/source. No SSH deploy key is required by these workflows.

GHCR fits GitHub-owned source/workflow permission boundaries. Refer to official [GHCR authentication/package linkage](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry) and [GitHub image publication/provenance](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images). Configure [environment protection rules](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments) explicitly; no automatic Production deployment exists.

## Image-job skip correction

The inspected workflow had one extra image-only predicate: `vars.V3_REGISTRY_PUBLISH_ENABLED == 'true'`. This Phase7 preparation opt-in deliberately disabled image publication by default. The complete old condition was:

```yaml
if: github.ref == 'refs/heads/main' && vars.V3_REGISTRY_PUBLISH_ENABLED == 'true' && needs.validate.outputs.passed == 'true'
```

Migrations required only main and the validation success output. Their reported success therefore establishes those shared predicates passed; the extra variable comparison explains skipped images in this workflow. An unset variable evaluates to an empty string, and environment-level variables are only available after the runner declares the environment, not as a reliable job-scheduling opt-in. A secret or server environment variable with the same name does not set the `vars` context. See GitHub's [variables](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-variables) and [contexts](https://docs.github.com/en/actions/reference/workflows-and-actions/contexts) references.

The local checkout was `9cb982c`; merge `3e3378a` was not available locally and authenticated remote access was unavailable during diagnosis. The exact remote variable value/scope and merge contents were not independently retrieved. The explanation above matches the inspected workflow and the owner's reported run, not an assertion of having read private repository settings.

The correction removes only the variable predicate, making image and migration eligibility identical:

```yaml
if: github.ref == 'refs/heads/main' && needs.validate.outputs.passed == 'true'
```

All seven validation suites and their aggregate success gate remain required. `environment: v3-registry`, scoped token permissions, scans, provenance and main-only builds are unchanged. No custom publication variable, PAT or tag is needed. Repository/organization policy must allow GHCR package creation and these Actions permissions; configured environment approval still applies. No repository settings were changed locally.

The manifest continues to need `[validate, images, migrations]` with GitHub's implicit `success()` condition. A skipped/failed image dependency blocks it; skipped jobs can still leave the overall workflow successful. The complete release artifact, not the green workflow badge alone, proves all components were assembled. See [job dependency behavior](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax). Regression tests check normal-main eligibility without the opt-in, retained environment/token protections, and the complete manifest dependency/commit checks.

## Build/release identities

Images: `ghcr.io/<lowercase-owner>/weymela-v3-api`, `...-worker`, `...-web`. Release tag `v3-<full-commit>-run<run-id>-attempt<attempt>` prevents re-run overwrite; build refuses existing tag. Pin runtime consumption to the **digest**, since registry tags alone are mutable administratively. No `latest` tag is published. Protect deletion of accepted packages; keep current and previous schema-compatible image sets.

Each build resolves official .NET10 SDK/ASP.NET Ubuntu,node24 and nginx stable base tags to digests before build and records them. Runtime images contain publish/static output,not SDK/source/tests/node_modules. Worker needs ASP.NET shared framework due to its existing FrameworkReference. API contains curl for bounded health check. No image build/pull was performed here. Actual sizes,base security posture and first CI results remain acceptance gates, not estimates labeled PASS.

Images are loaded on CI for pre-push Trivy scanning, then the same local image is pushed. GitHub OIDC attests its exact registry digest only when supported and explicitly enabled. BuildKit embedded provenance remains disabled for `--load`. Unsigned digest/checksum/source records and SBOMs provide traceability, **not cryptographically verified build provenance or a SLSA claim**. Only use `gh attestation verify oci://ghcr.io/OWNER/weymela-v3-api@sha256:DIGEST --repo OWNER/REPOSITORY` when the manifest records `attested`; absence of attestation must not be represented as successful verification.

The release package is assembled only after all three images and migration job succeed. Partial registry publication does not constitute a release. It includes source commit,run ID,platform,image digests/IDs/sizes,SBOM/security/base metadata,migration order/checksums and `deploymentAuthorized=false`. Releasing/tagging images is not permission to migrate/unfreeze. Owner archives approved evidence and verifies capacity,backup/legal/adapter gates before a separate V3-only deployment authorization.

## Private repository release correction

GitHub's [attestation support matrix](https://github.com/actions/attest-build-provenance/tree/v3) requires Enterprise Cloud for private/internal repositories. The reported error, `Feature not available for user-owned private repositories`, is an attestation persistence limitation, not a failed GHCR image push. A PAT or additional token scope does not solve this entitlement restriction. Keep this repository private.

Attestation policy:

- User-owned private repository: `unavailable`, even if an opt-in is accidentally set. No attestation request is made.
- Other repositories: `not-enabled` by default. Set `V3_GITHUB_ATTESTATION_ENABLED=true` only after verifying support, including Enterprise Cloud for a private organization. This variable controls attestation only, never scan/publish eligibility.
- Supported, enabled action: failure still fails the image job; there is no `continue-on-error`. Only action success plus its persisted GitHub ID/URL can produce `attested`.
- Missing repository metadata fails closed instead of guessing support. Existing environment approvals and job-scoped permissions remain unchanged.

Every successful `*-image.json`, embedded in the final manifest, now includes `buildStatus`, `publishStatus`, `scan.status`, the scan report path/SHA-256, SBOM path/SHA-256/format, and `attestation.status` with an absence reason or actual persisted reference. The metadata step requires successful build, scan, publication and SBOM outcomes; malformed scan evidence or HIGH/CRITICAL findings cannot be labeled passed. The manifest still requires all matrix jobs and migrations, and additionally checks these statuses, evidence checksums and exactly three matching-source components.

For a failure, `v3-diagnostics-<component>` preserves available image tags/digests, base-image references, stage outcomes and all vulnerability records (package, CVE, severity, installed/fixed version, OS/library classification). Secret matches and surrounding source code are excluded. These are **diagnostic artifacts**, never inputs to `v3-release-package`. Missing/incomplete Trivy JSON is explicitly not a clean scan; inspect the complete job log to distinguish scanner/database/network errors from vulnerability findings. Existing successful-release SBOM/scan artifacts remain required.

Release #3 evidence limit: connected GitHub repository/run reads returned 404 and local Git had no noninteractive credentials; no local V3 Trivy report was found. The workflow writes JSON to the runner and previously skipped its sole artifact upload after a failed scan/attestation. The owner's reported successful API/Worker push means those images were published before failure; their current registry existence, exact tags/digests and the Web failure cause require authenticated evidence. Web's failed scan precedes `docker push`, so this reported job could not publish Web, but this is not proof that no older Web package exists. No packages were deleted or changed here.

Do not guess a Web CVE or change nginx/dependencies without the report. No base-image, dependency, scanner version, severity, ignore or cache setting was changed in this correction. Web remains blocked until its complete scan/log is reviewed and any actionable fix passes a hosted/external-builder scan. The next new main run uses a new full-SHA/run/attempt tag for every component; already-published API/Worker tags remain untouched. A rerun uses its new attempt tag. Neither is a deployment.

On an authenticated workstation, retrieve evidence without rerunning or changing GitHub:

```sh
gh run list --repo hamiki4/WeymelaV3 --workflow release.yml --limit 10 \
  --json databaseId,number,headSha,conclusion,url
# Select the databaseId whose workflow number is 3 (not the literal run ID 3).
gh run view RUN_DATABASE_ID --repo hamiki4/WeymelaV3 --log
gh api repos/hamiki4/WeymelaV3/actions/runs/RUN_DATABASE_ID/artifacts
```

The publish-step log contains the versioned tag and digest. If Trivy JSON was not uploaded before runner disposal, the old findings cannot be reconstructed from a generic exit code; supply any retained report or collect the diagnostic artifact from a subsequent authorized hosted run. Do not run Docker builds on the application server or disclose tokens when sharing logs.

### Trivy console/report diagnosis (release #3)

The additional console evidence reports Alpine 3.24.1, vulnerability and secret scanning enabled, zero language-specific files, no EOL finding, then exit 1 without an error or finding table. This is compatible with the configured report destination; it does not establish a particular CVE, affected package or secret.

The pinned [action entrypoint](https://github.com/aquasecurity/trivy-action/blob/b6643a29fecd7f34b3597bc6acb0a98b03d33ff8/entrypoint.sh) invokes `trivy image "$IMAGE"`. Its [input mapping](https://github.com/aquasecurity/trivy-action/blob/b6643a29fecd7f34b3597bc6acb0a98b03d33ff8/action.yaml) supplies options through `TRIVY_*` environment variables. For Web, the configured equivalent command is:

```sh
trivy image \
  --scanners vuln,secret \
  --severity HIGH,CRITICAL \
  --ignore-unfixed=false \
  --exit-code 1 \
  --format json \
  --output .artifacts/release/web-security.json \
  --cache-dir "$GITHUB_WORKSPACE/.cache/trivy" \
  "$IMAGE"
```

`IMAGE` is `ghcr.io/hamiki4/weymela-v3-web:v3-<full-sha>-run<run-id>-attempt<attempt>`. Trivy is pinned to v0.74.0; default scan type is image, package types are OS and library, timeout is 5 minutes. No repository Trivy config/ignore files, EOL exit option, ignore policy or alternate scanner invocation are present. The action caches its database under the workspace cache directory; no cache/version change is justified by the supplied log.

`--format json --output ...` sends findings to a file, not a console table. Status messages remain visible. Trivy v0.74.0 [writes its report before checking finding exit status](https://github.com/aquasecurity/trivy/blob/v0.74.0/pkg/commands/artifact/run.go). Its [failure predicate](https://github.com/aquasecurity/trivy/blob/v0.74.0/pkg/types/report.go) includes secrets as well as vulnerabilities; both are severity-filtered. The configured [exit handling](https://github.com/aquasecurity/trivy/blob/v0.74.0/pkg/commands/operation/operation.go) returns code 1 for retained blocking findings, and the [entrypoint](https://github.com/aquasecurity/trivy/blob/v0.74.0/cmd/trivy/main.go) handles that intentional exit without a fatal-error message. Operational/configuration/report-write failures can also be nonzero, normally with error logging. Thus the silent exit is consistent with a finding, not proof of a specific vulnerability. Zero language-specific files does not exclude OS vulnerabilities or secrets; the OS/EOL message is not itself a vulnerability.

On the normal finding path, `web-security.json` is written before exit 1. Its presence/content in the old hosted run cannot be confirmed locally; an operational/report-write error can leave it absent or incomplete. The previous release workflow's success-only upload explains why the runner file was not retained after failure. No scan report or supported CVE identification has become available locally from this console excerpt.

The combined correction keeps the nonzero scan failure intact. A separate `if: failure()` step produces `.artifacts/diagnostics/web-diagnostics.json`; the next failure-only step uploads **only that sanitized file** as `v3-diagnostics-web` (14-day retention). It includes report status, OS/classification where available, all vulnerability records, safe secret rule metadata and separate HIGH/CRITICAL vulnerability/secret counts. It removes secret matches, surrounding code, image environment data, and repetitions of matched secret values in retained fields. Raw JSON and raw scanner logs are never printed or uploaded by this diagnostic path.

Missing, malformed, structurally invalid or unreadable reports produce explicit diagnostic errors, not a clean-scan claim. A failed step with no retained blocking findings remains inconclusive and requires the full job error. Console diagnostics print only fixed status/counts and the artifact name. Tests simulate a report-writing scanner exiting 1 and then execute the actual workflow diagnostics command; they do not pretend to reproduce the real Web scan.

One combined **CI-only diagnostic/attestation PR is safe to create for review**. It is not evidence that Web is secure or that the release is complete. The next hosted run must still pass the unchanged scan; if it fails, use the retained diagnostic artifact to identify the actual finding before changing dependencies or base images. No local Docker build, security-ignore rule, dependency change, push or deployment is part of this correction.

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
