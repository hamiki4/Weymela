# Weymela V3 cleanup manifest — 2026-10-08

This manifest records the safe cleanup performed while validating the final V3 candidate. It is not an application-data cleanup authorization.

## Removed disposable records

Eight stopped or orphaned Docker test resources were removed after confirming that none was a running Weymela Pilot or Production service:

| Count | Resource | Safety evidence |
|---:|---|---|
| 4 | Old Node/browser preview containers | Created by earlier local browser acceptance runs, stopped for 2–6 days, and not attached to a deployed Compose project. |
| 1 | Orphan PostgreSQL test container (`pensive_galois`) | Disposable integration-test database, stopped, with no Pilot/Production Compose ownership. |
| 1 | Old Testcontainers Ryuk container | Test harness cleanup process with no application data. |
| 1 | Old BrowserHost container | Disposable rendered-test host, stopped, and not part of Pilot or Production. |
| 1 | Old Playwright shell container | Disposable browser-test runner with no application data. |

The stale generated file `.artifacts/browser-host.json` was also removed after confirming that it had no live owner. It was ignored test-run metadata, not application data or release evidence.

## Explicitly not removed or changed

- No Weymela database row was deleted.
- No account, membership, financial journal, deposit, payout, audit record, notification, reconciliation evidence, or user-uploaded media was deleted.
- No Docker volume, database volume, backup, release package, registry artifact, or migration history was deleted.
- No balance was reset or edited.
- Running Pilot and Production containers were not stopped, recreated, or modified during cleanup.

## Post-cleanup verification

- The full PostgreSQL integration suite passed 527/527, including financial journal, idempotency, authorization, and reconciliation coverage.
- The HTTP/API integration suite passed 234/234.
- Repository safety reported 585 source files and zero violations.
- Production remained outside the operation scope.

The cleanup is complete. There are no identified application records that are both disposable and proven safe to delete, so no database cleanup was attempted.
