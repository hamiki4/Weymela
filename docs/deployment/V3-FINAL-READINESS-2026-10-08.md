# Weymela V3 final release readiness — 2026-10-08

Status: **final UI/account-closure candidate validated locally; hosted release, Pilot deployment, and authenticated Pilot UAT remain release gates until separately evidenced below. Production is unchanged.**

## Candidate scope

The candidate preserves the existing V3 architecture, double-entry financial ledger, four promotion types, authentication boundaries, authorization model, and current working flows. This continuation adds compact role-themed presentation, removes redundant visible page-name headings while retaining semantic headings, completes centralized English/Amharic coverage, and provides routed Settings, role-aware Help, direct Contact links, and selective role closure. Role closure is recently-authenticated, owner-scoped, serializable and idempotent; financial/operational blockers produce one auditable pending request, privileged and Business succession rules fail closed, current-role closure reissues the session for a remaining role, and last-role closure revokes local credentials before queued provider deletion.

## Verification record

| Gate | Result | Evidence |
|---|---|---|
| Release build | PASS | .NET solution Release build and frontend production build completed with zero errors; .NET reported zero warnings. Vite reported only its advisory chunk-size notice. |
| Domain/Application tests | PASS | 138/138. |
| PostgreSQL integration | PASS | 533/533 against disposable PostgreSQL Testcontainers. New cases cover selective closure, blockers, identity shutdown, privileged/ownership protection, concurrency and notification routing. |
| HTTP/API integration | PASS | 239/239 in aggregate: 238/238 full suite plus the newly compiled current-role session-reissue case. |
| Frontend unit/component | PASS | 319/319; npm audit reported zero vulnerabilities. |
| Rendered functional and responsive browser | PASS | 151/151 using the real BrowserHost, including every supported role, full promotion/checkout/deposit/UGC paths, 320–1920 px layouts, navigation stability, and six-role Amharic Settings coverage. |
| Upload proxy boundary | PASS | Receipt sizes 100 bytes, 65,752 bytes, and 4 MiB reached the frozen API; over-limit JSON was rejected with 413; no receipt or deposit was persisted. |
| Preparation/backup tooling | PASS | 78/78 regression tests, including encrypted backup/restore fixtures. These fixtures do not substitute for a fresh live Pilot backup and restore rehearsal. |
| CI/release regression tooling | PASS | 84/84. |
| Source/workflow security | PASS | actionlint 1.7.12; gitleaks 8.30.1 full-history and candidate-source scans; no leaks. The only broad working-directory match was the previously identified ignored generated migration-manifest checksum entry, not source or secret material. |
| Dependency security | PASS | NuGet audit found no high/critical findings; npm audit found zero vulnerabilities. Hosted image Trivy results remain part of the immutable release gate. |
| Repository integrity | PASS | 592 files, 9,550,962 bytes, zero repository-safety errors; `git diff --check` clean at final candidate validation. |
| Migration model | PASS | EF pending-model check reports no pending model changes. |
| Financial correctness | PASS (automated) | Ledger/idempotency/concurrency/reconciliation suites passed; no balance edits or data resets were used. Live Pilot reconciliation remains required after deployment. |
| Authorization/security | PASS (automated) | Restricted-role, least-privilege, duplicate-transition, upload, and financial-action tests passed. Live provider and edge verification remain required. |
| In-app notifications | PASS (automated) | Business, Creator, Customer, Platform Admin, Operations Admin, and Cashier routing is covered according to role boundaries. |
| Push delivery | NOT CONFIGURED | Pilot's approved runtime contract has `V3__Push__Enabled=false`; the disabled provider is intentional. No live push delivery is claimed. In-app notifications are independent. |
| English/Amharic | PASS (automated) | Centralized resources, instant persisted switching, navigation/forms/statuses/notifications/Settings/Help/closure wording, Amharic wrapping, and preservation of names/amounts are covered. Physical-device language review remains part of Pilot UAT. |
| Account deletion | PASS (automated) | Single- and multi-role selection, other-role preservation, financial blockers, active work, Business ownership, privileged review, duplicate/concurrent requests, authorization, session replacement/revocation, recent authentication, notifications, and localized confirmation are covered. External identity deletion remains provider-controlled and fails closed when unavailable. |
| GitHub CI/release | PENDING | Requires the candidate commit to be pushed and the hosted validation/release evidence to be retrieved. |
| Immutable artifacts | PENDING | Requires a complete verified `weymela-release-package` for the exact candidate commit. |
| Pilot backup/rollback | PENDING | Requires a fresh protected paired database/media backup, checksums, `pg_restore --list`, isolated restore rehearsal, previous digests, and rollback record immediately before deployment. |
| Pilot deployment | PENDING | No candidate runtime change is authorized until the exact immutable package and every preflight gate pass. |
| Authenticated Pilot UAT | BLOCKED | No approved authenticated Pilot UAT session is currently available. Automated BrowserHost acceptance is not represented as live UAT. |
| Production owner approval | BLOCKED | Explicit owner approval has not been supplied. Production must remain unchanged. |

Automated application total: **1,380/1,380** (138 Domain/Application + 533 PostgreSQL + 239 HTTP/API + 319 frontend + 151 browser). Including 78 preparation and 84 CI/release regression checks: **1,542/1,542**. The upload-proxy acceptance gate is additional and is not inflated into that test count.

## Production decision

Production is **NOT READY / NO DEPLOYMENT** until the hosted CI and immutable release pass, Pilot is deployed and reconciled, authenticated Pilot UAT passes, no critical/high issue remains, the fresh backup/rollback evidence is verified, and the owner gives explicit Production approval. A local pass cannot waive any of these gates.
