# Phase 9A — incomplete, blocked at session integration review

> Historical checkpoint: the review narrative through "Verification scope" is
> retained as evidence of the stopped integration attempt. Its draft schema and
> validation statements are superseded by the authoritative Phase 9A.1 section
> below; they must not be used as current deployment instructions.

This is a working-tree checkpoint, not an acceptance or release claim. No live
configuration, database, Firebase, services, financial settings or deployments
were changed. No Phase 9B requirements were added.

## Review gate

The first integration patch was rejected with:

> The patch changes core authentication and profile-switch authorization, including broadening the switch endpoint from Workspace to VerifiedAccount, creating an unapproved security-boundary change beyond the authorized PIN/idle-lock scope.

A narrower patch preserving `RequireAuthorization("Workspace")` was also rejected:

> This is an attempted workaround of the prior rejection and still modifies core authentication/session issuance, including profile-switch renewal, without resolving the identified security-boundary risk.

The owner then explicitly authorized session-cookie/device/PIN integration while
requiring the existing `Workspace` switch policy to remain unchanged. The review
system still rejected the combined integration patch with:

> This replaces core Firebase session issuance and profile-switch authentication for all users, changes sign-out authorization, and depends on unverified durable-session behavior, creating substantial lockout/service-disruption risk beyond a narrowly scoped Phase 9A change.

The owner instructed the implementation to stop on another authorization-boundary
rejection. The partially applied API hooks were therefore removed immediately.

No further integration attempt was made. The temporary API hooks created during
this pass were removed. AuthEndpoints, OnboardingEndpoints, LiveAuthentication,
ApiHost and ApiSafetyMiddleware are unchanged from the starting commit.
DeviceSessionService is **not registered or called by the API**. The existing
Forgot PIN endpoint still fails closed; it is not a working recovery flow.

Continuation needs explicit review authorization for the cookie/session lifecycle
integration, while preserving the existing Workspace profile-switch policy.
Recovery that cannot resume an existing approved device profile must have a
reviewed explicit-selection path; it must not broaden a commerce authorization
policy or silently choose the highest-privilege/first membership.

## Draft foundation retained

- Reuses AuthorizedDeviceRecord, IdentityBinding and AuthIdentifiers.
- Adds a proposed DeviceSessionRecord: account/binding, device, fixed expiry,
  meaningful-activity time, lock/revocation/unlock times, profile key and generation.
- Draft server service uses a 20-minute boundary (`now >= lastActivity + 20m`),
  checks before updating activity, and does not extend the one-hour session expiry.
- Device-scoped failures serialize on the account binding row. Five failures
  produce a 15-minute cooldown; ten consecutive failures require recovery.
  Successful verified PIN unlock resets failures; profile changes do not.
- Device credential proposal: cryptographically random 256-bit bearer secret,
  hash in PostgreSQL, separate Secure/HttpOnly/SameSite=Strict host cookie.
  It recognizes possession of browser storage, **not hardware identity**. A copied
  device cookie plus PIN remains a threat. No fingerprint or browser-storage PIN.
- PIN verifier: independent random salt, PBKDF2-HMAC-SHA256 (600,000 iterations),
  HMAC preprocessing with a separate externally held pepper, fixed-time digest
  comparison, fixed-size validation and at most two concurrent KDF operations.
  This follows established primitives, not a custom cipher, and is separate from
  email-code hashing. Five digits remain low entropy; device possession and
  durable attempt limits are indispensable.

References: [OWASP password storage](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)
and [OWASP session management](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html).

## Pending work and decisions

- Wire durable checks into existing protected-cookie issuance/validation,
  Customer activation, profile changes and sign-out after review authorization.
- Recheck transaction/concurrency behavior with real PostgreSQL. The current
  service is an unexecuted draft, not security acceptance.
- Complete atomic email recovery, new device/session issuance, V3-local revocation
  of old sessions/devices and notification. A request alone must change no access.
- Add activity handling driven by real interaction/navigation, never timers,
  passive rendering, notification reads or QR polling. Server enforcement still
  needs implementation; there is currently no active 20-minute lock in the API.
- Implement and render PIN setup, unlock, cooldown and recovery UI. No frontend
  changes were made in this blocked pass.
- Device recognition duration is owner-approved at exactly 30 days, separately
  from the fixed one-hour session and 20-minute idle threshold. The draft options
  and service use 30 days, but no API currently consumes them.
- Supply `V3__Auth__PinPepper` externally (base64 of at least 32 random bytes,
  independent from CodeHashKey) and an approved finite
  `V3__Auth__AuthorizedDeviceDays` (1–30). No secrets were generated or stored.
- Existing live email delivery/Firebase custom-token signing configuration and
  real identity/legal acceptance gates remain outstanding. No live acceptance
  can be inferred from test adapters. Native hardware PIN integration is not added.

## Schema and grants — not applied

The model draft adds DeviceSessions plus AuthorizedDevices.PinVerifier and
ExpiresAtUtc, and EmailAuthChallenges.SessionEstablishedAtUtc. One additive
migration and the EF snapshot still need to be generated/verified on an approved
SDK runner; no migration was created or applied in this blocked pass. Accordingly
the model currently has pending changes and must not be released.

Proposed API grants: SELECT/INSERT/UPDATE on DeviceSessions and AuthorizedDevices;
existing SELECT/UPDATE on IdentityBindings and EmailAuthChallenges; required
AuthIdentifiers reads and audit/notification inserts. No CREATE, ownership or
DELETE grants are proposed. Worker needs no PIN verifier/device/session access.
Backup retains read-only access. Final exact grants require integration review.

## Verification scope

New isolated KDF tests cover exact ASCII digit length, salted non-plaintext
verification, leading zeros, wrong PIN, wrong pepper, corrupt format and refusal
to accept email-code verifier formats. They do not prove HTTP lock enforcement,
recovery, PostgreSQL concurrency, or browser behavior. No .NET/Node executables
are installed on this host; SDK, frontend and browser execution require the
approved external runner. No tests are skipped or weakened to hide this gap.

Required follow-up suites include real HTTP cookie/device binding, all idle and
attempt boundaries, concurrent failures/recovery, stale-cookie/tab behavior,
single/multiple/suspended profiles, unchanged role approvals and balances,
anonymous restrictions, background reads, no secret logging, and rendered flows
at 375/390/393/430 plus tablet/desktop widths.

Phase 9A is **not implemented end-to-end and not ready for PR/release acceptance**.

Local checks executed in this pass: preparation 24 passed; CI orchestration 77
passed; `git diff --check` passed. These are source/harness checks, not execution
of the new PIN implementation. All SDK, EF, API, frontend and browser acceptance
remains unexecuted in this environment. No commit, push or CI dispatch occurred.

## Phase 9A.1 persistence-only continuation

Phase 9A.1 supersedes the draft-foundation details above while preserving the
review history. It deliberately contains no HTTP authentication, Firebase
session, protected-cookie, profile-switch, sign-out, frontend, PIN-recovery or
Phase 9B integration. `DeviceSessionService` was replaced by side-effect-free
`DeviceAccessPolicy` and record factories; no unfinished service is registered.

Fixed constants are 20 minutes to the closed idle boundary, one hour to the
protected-session boundary, 30 days to authorized-device expiry, five failures
to a 15-minute device cooldown and ten failures to recovery-required. UTC is
mandatory. AuthorizedDevices gains nullable `PinVerifier` and `ExpiresAtUtc`
for later enrolled records plus non-null `RequiresRecovery`. Database constraints
bound attempts to 0–10, tie recovery to attempt 10, permit cooldown only at or
after attempt 5, and require any device expiry to equal enrollment plus 30 days.

DeviceSessions stores UserId, identity binding/version, authorized-device ID,
SHA-256 session-identifier digest, created/activity/lock/revocation timestamps,
fixed expiry, generation and optimistic-concurrency version. It stores no role,
financial value, PIN or raw credential. Restrictive foreign keys and unique/
lookup indexes are included. `OpaqueDeviceCredential` generates 256 random bits;
only its digest enters record factories. No browser delivery exists in this slice.

The PIN verifier accepts exactly five ASCII digits and uses a random 16-byte salt,
PBKDF2-HMAC-SHA256 with 600,000 iterations, an independent external base64 pepper,
fixed-time comparison and at most two concurrent KDF operations. Missing/short/
malformed pepper and malformed verifier material fail closed. The API template
contains only a blank `V3__Auth__PinPepper` placeholder; no secret was generated.

Migration `20260914022116_AddDevicePinSessionFoundation` is additive and was
generated in a network-disabled disposable SDK container. It adds the fields,
constraints and DeviceSessions table described above. Its Down operation drops
device-session history and PIN enrollment fields, then restores the former
nonnegative-attempt constraint; any future live rollback therefore requires an
explicit backup and security sign-out plan. It was not applied to Pilot/Production.

Future grants, documented only and not applied:

```sql
GRANT SELECT, INSERT, UPDATE ON TABLE v3."AuthorizedDevices" TO weymela_v3_api;
GRANT SELECT, INSERT, UPDATE ON TABLE v3."DeviceSessions" TO weymela_v3_api;
```

No DELETE, DDL, ownership or schema CREATE is required. Worker needs no new grant.
Phase 9A.2 still owns cookie integration, meaningful-activity classification,
lock/unlock and recovery endpoints, rate limits, frontend/cross-tab UX, sign-out
revocation and complete HTTP/browser acceptance. Existing Workspace authorization
and every application/financial rule remain unchanged.
