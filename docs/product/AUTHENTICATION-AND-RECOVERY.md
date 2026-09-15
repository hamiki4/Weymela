# Weymela V3 Authentication and Recovery (authoritative)

This document governs V3 authentication; it supersedes the earlier SMS/Phone OTP proposal without changing the shared Firebase project or V2 configuration.

An account has one Firebase UID and one Weymela UserId, and may hold multiple approved profiles (Customer, Creator, Business memberships, or other explicitly provisioned roles). Sign-in opens one active profile at a time; if more than one is eligible, the user must choose it. `Switch profile` reissues the protected session after server-side membership and ownership checks. It does not grant permissions, merge balances, or alter role approvals. Deactivating the identity binding revokes every profile, while suspending one membership affects only that profile.

## Account identity

Signup requires one phone number and one email address. The phone number is a preferred, unverified account identifier; it is never proof of ownership and cannot by itself link, merge, recover, or take over an account. Email is the sole verification and recovery channel. After the email challenge succeeds, both aliases are persisted against the same server-created V3 user and Firebase UID. Later email- or phone-identifier sign-in resolves that existing user and sends the challenge only to the stored verified email; a phone request cannot supply a replacement email. A single verified Firebase UID maps to one V3 user through a trusted `IdentityBinding`.

V3 does not use SMS, Firebase Phone Authentication, required Google sign-in, or a second account password. Public signup cannot create a role, grant Admin/payment authority, or replace an existing identifier. Conflicting identifiers fail without overwriting or merging accounts.

## Email-code flow

Signup, new-device enrollment, and PIN recovery use separate purpose-bound email challenges. Codes are cryptographically random, six digits, salted/keyed-hashed at rest, short-lived, single-use, attempt-limited, and resend-throttled. Verification and consumption are atomic. Responses are generic and never disclose account existence, codes, tokens, or full addresses. A request alone does not modify or lock an account.

Successful signup/email verification may receive a trusted server-issued Firebase custom token, which is exchanged for the existing secure Weymela session. Email delivery addresses needed for phone-identifier delivery remain server-side and are excluded from all projections. Pilot/Production fail closed until protected email delivery, code-hash material, and custom-token signing adapters are configured. No live provider or credential is included in the repository.

## Authorized devices and PIN recovery

A five-digit PIN is only an unlock factor for an already enrolled authorized device; it is not an Internet password and is never padded into a Firebase password. The Web/PWA implementation binds it to separate HttpOnly authorized-device and one-hour DeviceSession credentials. The authorized device lasts at most 30 days, while server-side meaningful inactivity locks the DeviceSession at exactly 20 minutes. A cosmetic PIN screen, browser fingerprint, local-storage flag, or phone-plus-PIN login is forbidden.

Forgot PIN is available only to an existing Firebase-authenticated account on its current recognized, active device. Recovery initiation uses the generic `PinRecovery` email challenge and sends its six-digit code only to the stored verified email; requesting a code changes no PIN, device, or session state. Completion requires the authenticated binding, current device credential, and valid purpose-bound email code together. It accepts a matching new five-digit PIN, atomically revokes every prior V3 authorized device and DeviceSession for the account, creates one replacement 30-day device and fixed one-hour session, replaces only the two HttpOnly device cookies, and preserves the Firebase UID, Weymela UserId, auth cookie, active profile, memberships, permissions, and balances. Other devices must complete full authentication and cannot use their revoked credentials.

The former anonymous `/api/auth/pin/reset` placeholder is not a recovery mechanism and is no longer mapped. Recovery-required devices use the exact authenticated `/api/device/pin-recovery/complete` boundary; enrollment and workspace routes are not recovery bypasses. Full-authentication-required state must complete the existing sign-in flow before recovery can proceed. Successful recovery records audit and role-scoped in-app security notification evidence without synchronously calling an external provider or exposing PINs, codes, destinations, or credentials.

## Firebase boundary

The project remains `weymela-pilot`. The browser uses only public Firebase Web configuration, keeps persistence in memory, and sends only a fresh ID token to `/api/auth/firebase/session`. Server-side verification checks issuer, audience, signature, expiry, authentication time, and the trusted V3 binding/role. Custom-token signing is server-only and requires externally supplied credentials; service-account keys never enter Git, bundles, logs, or images.

## Security and lifecycle requirements

Old Firebase tokens and session-exchange paths cannot bypass email verification, binding validity, device revocation, or privileged-role requirements. Sign-out and binding/version revocation invalidate access. Email delivery failures, missing signing material, unsupported device reset, expired/replayed/wrong-purpose codes, conflicts, and rate limits fail closed with safe generic responses.

## Additional profile onboarding

An authenticated verified account may request one additional public profile through the restricted onboarding context. The choices are `Use as Customer`, `Become a Creator`, and `Add a Business`; Cashier and PlatformAdmin remain trusted-provisioning-only. A zero-profile verified account may retrieve only the current account Terms/Privacy onboarding status in addition to its session/device bootstrap routes; this does not grant Workspace access. Customer activation requires explicit references to the exact current Terms of Service and Privacy Policy versions and hashes. Their immutable acceptances use the account-level legal role and are committed in the same transaction as immediate Customer activation. Creator and Business requests remain pending until Platform Admin approval, and their existing role-specific agreements remain separate. `RoleEnrollment` records preserve the request, submitted details, review decision, idempotency reference and concurrency version, while active `CommercePermission` rows remain the sole authority for workspace access. A pending or rejected request never disables an approved profile, and a suspended membership is not resurrected by replaying an old request. Approval or immediate Customer activation creates only the intended scoped subject (and a new BusinessId/wallet for a Business), atomically with audit evidence.
