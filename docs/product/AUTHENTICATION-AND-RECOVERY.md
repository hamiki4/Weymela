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

A five-digit PIN is only a native-style unlock factor for an already enrolled authorized device; it is not an Internet password and is never padded into a Firebase password. Browsers/PWAs must use a reviewed authenticated session/passkey/device-credential mechanism. A cosmetic PIN screen, browser fingerprint, local-storage flag, or phone-plus-PIN login is forbidden.

The current Web adapter intentionally fails closed for PIN reset/device enrollment until a secure credential provider is approved. Recovery must verify the registered email, revoke affected V3 sessions/devices, and notify the account before any new credential is accepted. Admin/payment approvers retain stronger authentication and recovery cannot silently bypass it. Native secure-device integration is a separate future capability and must not be represented as implemented by the Web UI.

## Firebase boundary

The project remains `weymela-pilot`. The browser uses only public Firebase Web configuration, keeps persistence in memory, and sends only a fresh ID token to `/api/auth/firebase/session`. Server-side verification checks issuer, audience, signature, expiry, authentication time, and the trusted V3 binding/role. Custom-token signing is server-only and requires externally supplied credentials; service-account keys never enter Git, bundles, logs, or images.

## Security and lifecycle requirements

Old Firebase tokens and session-exchange paths cannot bypass email verification, binding validity, device revocation, or privileged-role requirements. Sign-out and binding/version revocation invalidate access. Email delivery failures, missing signing material, unsupported device reset, expired/replayed/wrong-purpose codes, conflicts, and rate limits fail closed with safe generic responses.

## Additional profile onboarding

An authenticated verified account may request one additional public profile through the restricted onboarding context. The choices are `Use as Customer`, `Become a Creator`, and `Add a Business`; Cashier and PlatformAdmin remain trusted-provisioning-only. Customer activation is immediate after the account and applicable terms checks; Creator and Business requests remain pending until Platform Admin approval. `RoleEnrollment` records preserve the request, submitted details, review decision, idempotency reference and concurrency version, while active `CommercePermission` rows remain the sole authority for workspace access. A pending or rejected request never disables an approved profile, and a suspended membership is not resurrected by replaying an old request. Approval or immediate Customer activation creates only the intended scoped subject (and a new BusinessId/wallet for a Business), atomically with audit evidence. A verified account with no approved profile can view its own onboarding status and sign out, but cannot enter campaigns, wallets, checkout or administration.
