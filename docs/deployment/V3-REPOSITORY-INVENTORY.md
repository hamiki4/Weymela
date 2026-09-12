# V3 repository safety / candidate commit inventory

Authoritative working directory: `/opt/WeymelaV3`. Branch `main`,no commits,no remote,no staged files at Phase7 inspection. This package does not create a GitHub repository,stage,commit or push. Existing cumulative Phase0–6 source is preserved.

## Included source

- `Weymela.slnx`,README,safe `.env.example`,`.gitignore`,`.dockerignore`,pinned `.config/dotnet-tools.json`.
- `src/`:166 approved Domain/Application/Infrastructure/API/Worker/Web source/config/runtime-asset files. No Phase7 runtime source modifications. PostgreSQL schema/migrations and all prior financial/UI rules unchanged.
- `tests/`:43 source files across Domain/Application/PostgreSQL/HTTP/BrowserHost and new preparation checks; frontend test/E2E source remains under Web. Test **source** is retained; generated screenshots/results/control keys are excluded.
- `.github/workflows/`:PR/reusable validation,immutable registry release,and manual Pilot review gate.
- `docker/`:three Dockerfiles,image-only isolated Compose,three blank/safe Pilot configuration templates,Web headers/proxy,and instructions.
- `tools/`:CI verification/build/artifact scripts and existing acceptance utilities. Tools do not execute deployment automatically.
- `docs/`:authoritative architecture/product/UX/finance/security and new Pilot preparation,capacity,adapter/test-user/legal,database/backup/manual acceptance/CI/readiness documents.

Exact filename/size/SHA256 inventory is generated to **ignored local evidence** `.artifacts/phase7-repository-inventory.json`. It inventories both tracked and approved non-ignored untracked files,including tracked files forced past ignore rules. Regenerate immediately before any separately authorized initial commit:

```sh
git status --short
git ls-files --cached --others --exclude-standard
python3 tools/ci/repository-safety.py . --report .artifacts/phase7-repository-inventory.json
git diff --check
```

No HEAD exists,so ordinary `git diff --stat`/`git diff --check` alone would not cover new files. The independent all-source safety/whitespace/hash inventory closes this gap; no staging is used to manufacture a diff. Initial commit would include the approved complete inventory,not only Phase7 additions. Review the explicit file list before staging rather than copying V2 or blindly adding external material.

## Excluded / protected

`.env` and populated environment configs;Firebase Admin/service-account credentials;PFX/PEM/private keys,Android keystores/signing/upload keys;DB dumps/backups/release tarballs;node_modules/bin/obj/dist;logs,test-results,screenshots,Playwright artifacts and `.artifacts`. Runtime public PNG/PWA icons are intentionally source-controlled;acceptance screenshots are not. Templates contain blank credential values and nonsecret proposed URLs only.

No existing V2 file is copied into V3. A read-only SHA256 comparison of nontrivial >=1KiB `.cs/.tsx/.ts/.css/.csproj` files in V2 versus V3 found **zero exact source matches**; this complements the prior clean architecture provenance and V3 namespace/path review,it is not a claim that hash comparison proves semantic originality. No V2 secret/config files were opened for this comparison.

Gitleaks8.30.1 redacted directory scan: no leaks found. Supplementary filename/private-key/env-template/binary/whitespace checks: pass. Review is scoped to candidate source,not ignored acceptance evidence or old server backups. Absolute proof against every possible secret is not claimed; repeat secret/history scan after any authorized staging/import and before push. No history exists to scan yet; CI scans both full Git history and checked-out source.

## Readiness

Source is ready for owner review and an explicitly authorized new private GitHub repository/initial push. Owner/name,access/visibility,license,branch protection,reviewers,GitHub plan/attestation support and CI publish opt-in remain decisions. **Do not interpret repository readiness as Pilot deployment readiness.** See [Pilot gates](V3-PILOT-PREPARATION.md) and [capacity blocker](V3-PILOT-CAPACITY-AND-CLEANUP.md).
