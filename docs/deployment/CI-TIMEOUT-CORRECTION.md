# CI acceptance timeout correction

Scope: workflow/orchestration only, based on `8ab7528`. No product/financial/schema/UI changes; no deployment,Firebase,V2,Pilot/Production operation,server image build,commit or push.

## Incident evidence and diagnostic limit

Owner reports the first immutable registry release's `validate / acceptance` was canceled after approximately45minutes with the job maximum-execution-time error. Vitest had reported3/3files,54/54tests passed. Images/migrations/manifest did not complete. This confirms a job deadline,not a demonstrated product assertion failure.

The GitHub connector returned404 for this repository's Actions runs and no noninteractive local repository credential was available. The job log was requested; no authenticated run/step timestamps could be obtained. **Exact slowest stages and the precise cancellation location remain unverified.** Do not infer that the whole suite completed,or that browser cleanup caused this particular cancellation,from the Vitest summary alone.

Confirmed source causes/risks:

1. `ci.yml` put restore/full build,Domain,Application,PostgreSQL,HTTP API,EF/audits,frontend install/tests/build,Chromium installation and all browser tests on one runner under one45minute limit. Independent work consumed a cumulative deadline. One monolithic shell step did not expose per-stage timing in the Actions step list.
2. `browser-tests.sh` ran last. It sent SIGINT to a background `dotnet run` wrapper and then used **unbounded `wait`**. Asynchronous Bash children may ignore SIGINT; a wrapper/child shutdown issue can leave a job waiting after tests finish. This is a reproducible orchestration risk,not a measured diagnosis of the inaccessible canceled run. See [Bash signal semantics](https://www.gnu.org/s/bash/manual/html_node/Signals.html).
3. Source-identified heavy candidates are isolated PostgreSQL tests (fresh database plus migrations per test),HTTP tests using the same disposable-database pattern,and serial rendered E2E. The responsive suite covers8widths with up to240seconds per viewport case; other Playwright cases retain90second defaults. Chromium/system dependency installation adds cold-run overhead. No historical duration is fabricated for these stages.

## New job graph / limits

All seven workload jobs are independently scheduled on GitHub-hosted Ubuntu24.04. Each DB/browser job owns its own Testcontainers resources. Existing in-suite concurrency and all tests remain unchanged; no sharing of live DBs or mutable fixture state.

| Job | Complete responsibility | Job maximum |
|---|---|---|
| source-security | history/directory secret scan,actionlint,source/whitespace inventory,preparation and CI orchestration tests |15minutes |
| dotnet-unit | all Domain and Application tests |20minutes |
| postgres | all Infrastructure/PostgreSQL integration tests |60minutes |
| http-api | all HTTP API integration tests |45minutes |
| frontend | npm install/audit,all Vitest tests,production frontend build |20minutes |
| dotnet-checks | full solution Release build,EF pending-model,NuGet audit gate,license inventory |20minutes |
| browser | BrowserHost + frontend build,Chromium installation,all E2E/responsive/security flows |75minutes |
| acceptance | require exact set of seven jobs,all with result `success` |5minutes |

Browser step limits: build15min,Chromium installation10min,E2E60min,still subject to75min overall. These bounded integration/E2E ceilings allow migration/viewport/cold-install workload independent of other suites; they are **not measured expected runtimes**. Review actual new timings after the next hosted run and tighten appropriately. GitHub concurrency/plan quotas can queue independent jobs; no application-server runner is introduced.

`validate.sh` now requires a named profile; no accidental default serial mega-job remains. Every command stage emits start/end/elapsed/exit-status output and appends `stage-timings.tsv`,uploaded under distinct per-job artifact names. Browser install/build/E2E also have separate GitHub steps. Npm download cache uses the committed lockfile;no node_modules/build artifacts or database state is shared between jobs.

## Browser host lifecycle correction

The shell delegates to `browser-host.py`,which launches the already-built BrowserHost DLL directly,without a `dotnet run` intermediary,in a dedicated owned process group. Startup is bounded at180seconds and fails on stale/non-loopback control or early host exit. All `npm run e2e` tests execute with their original configuration.

Cleanup sends TERM to only owned child groups,waits at most30seconds per child,then KILL with a bounded5second wait. Forced termination fails acceptance even if tests passed. A failed E2E result is preserved; cancellation is nonzero. No broad process kill,Docker prune or live service operation. Only anonymous timing/status evidence is uploaded;raw BrowserHost log/control is excluded.

## Fail-closed release

The acceptance gate uses `always()` only to inspect results,**not to forgive failures**. Failure,cancellation,skip or missing required job prevents emitting `passed=true`. The reusable workflow exposes that output only from successful acceptance. Images and migrations still need the entire reusable validation job and additionally require its explicit pass output. Manifest now needs validation,images and migrations,and the same pass output. No `continue-on-error`,test filter,skip,max-failure shortcut or assertion weakening was added. [GitHub dependency semantics](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idneeds) preserve downstream failure propagation.

Registry publish opt-in,main restriction,environment review,image scan/provenance and no-deployment rules remain unchanged. Keep the existing required acceptance check in branch protection and confirm its displayed name on the next run. A workflow rerun on the old commit uses the old workflow: push/review/merge this patch to test the new graph.

## Targeted validation

- 18 new orchestration tests: execute real dispatch/gate code using lightweight command stubs; verify all suites invoked,command failure preserved,success/failure/cancel/skip/missing gate cases,release dependencies,and real disposable process cleanup including ignored signals. These are not substitutes for product acceptance.
- 24 existing preparation tests unchanged and passing.
- actionlint all workflows PASS;Bash syntax PASS;source safety/secret scan PASS;`git diff --check` PASS after copy to authoritative tree.
- Full453-test product suite not rerun locally;all product test source and invocation coverage retained. Actual hosted timing/success remains pending the next authorized push/run. Historical stage ranking remains pending access to the canceled log.

## Exact owner commands (not executed here)

From the current prepared working tree,create a review branch and stage only this correction:

```sh
cd /opt/WeymelaV3
git switch -c fix/ci-acceptance-timeout
git diff --check
git add .github/workflows/ci.yml .github/workflows/release.yml tools/ci/validate.sh tools/ci/browser-tests.sh tools/ci/browser-host.py tests/ci/test_ci_orchestration.py docs/deployment/CI-TIMEOUT-CORRECTION.md docs/deployment/GITHUB-ACTIONS.md
git diff --cached --check
git commit -m "Fix CI acceptance timeout with parallel validation and bounded browser cleanup"
git push -u origin fix/ci-acceptance-timeout
```

Open a pull request into main. PR validation exercises the new jobs;merge only after all pass. Main merge triggers the existing gated immutable registry release,no deployment. Do not weaken protections or force-push. Review downloaded timing artifacts to identify actual slow stages before adjusting limits again.
