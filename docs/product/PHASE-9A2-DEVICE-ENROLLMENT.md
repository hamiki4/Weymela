# Phase 9A.2 — authorized-device and initial PIN enrollment

This slice is initial enrollment only. It does not implement idle locking, PIN
unlock, PIN recovery, meaningful-activity tracking, step-up authentication, or a
replacement authentication/session system.

## Contract

- `GET /api/device/enrollment` requires an authenticated account and returns only
  `state` and `expiresAtUtc`. It never returns email, verifier, salt, or credential
  material.
- `POST /api/device/enrollment` requires the existing verified Firebase-backed
  account context, `Idempotency-Key`, and JSON `{ pin, confirmPin }`.
- Both PIN fields must contain exactly five ASCII digits and match. The existing
  Phase 9A.1 PBKDF2-HMAC-SHA256 verifier and externally supplied
  `V3__Auth__PinPepper` are used. Missing cryptographic configuration fails closed.
- Successful enrollment creates one `AuthorizedDevices` record and audit and
  idempotency evidence in one transaction. It creates no device session and does
  not reissue or mutate the existing authentication cookie, role, profile list, or
  active profile key.

The device credential is 256 random bits. Only its SHA-256 digest is persisted.
The raw credential is returned only as the host-only `WeymelaV3.Device` cookie:
HttpOnly, SameSite=Strict, path `/`, 30-day expiry, and Secure outside Development.
It is not an authentication credential by itself.

An active recognized device is an idempotent success and does not create another
row. A presented expired, revoked, or recovery-required device fails closed;
recovery/re-enrollment policy is owned by a later approved slice. Synthetic
development personas are explicitly exempted for fixture compatibility and cannot
use the enrollment mutation.

## Persistence and grants

No Phase 9A.2 migration is added. The implementation uses the unapplied Phase
9A.1 migration `20260914022116_AddDevicePinSessionFoundation` unchanged. Before a
future deployment, the API runtime role needs the already documented permissions:

```sql
GRANT SELECT, INSERT, UPDATE ON TABLE v3."AuthorizedDevices" TO weymela_v3_api;
GRANT SELECT, INSERT, UPDATE ON TABLE v3."DeviceSessions" TO weymela_v3_api;
```

This slice writes `AuthorizedDevices`, `IdempotencyRecords`, and `AuditEvents`; the
latter two use the runtime grants already required by existing operations. The
Worker needs no additional device/PIN grant. No runtime role needs DELETE, DDL,
schema CREATE, or ownership.

## Next security slice

Phase 9A.3 must make device/session state authoritative for the 20-minute idle
lock, meaningful activity, PIN unlock, and fixed one-hour session boundary. Until
that reviewed integration exists, this frontend enrollment gate is not a claim
that direct workspace APIs enforce idle/device locking. Phase 9B step-up remains
out of scope.
