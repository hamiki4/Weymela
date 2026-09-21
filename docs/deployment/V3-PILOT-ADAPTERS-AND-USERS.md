# Pilot adapters, legal publication and test identities

No Firebase call/configuration/resource change, user provisioning, live email, legal publication, fabricated activity or financial posting was performed. The Resend and Firebase Admin adapters and Pilot Web build wiring are source-only implementation; they have not been run against Pilot.

## Firebase: reuse existing identity, do not create a new project

Pilot requires the exact existing Firebase project `weymela-pilot` through `V3__Auth__FirebaseProjectId`. It is a public identifier, not a secret. Expected issuer `https://securetoken.google.com/weymela-pilot`, audience `weymela-pilot`, RS256, valid expiry/subject and recently authenticated ID token. The verifier still uses Google's fixed X.509 endpoint and does not need private credentials. Custom-token signing is a separate API-only boundary: `V3__Auth__FirebaseCustomTokenMode=FirebaseAdmin` and `GOOGLE_APPLICATION_CREDENTIALS=/run/secrets/v3-firebase-admin.json` select an externally mounted, read-only service-account JSON. The file is never copied into an image, exposed to Web/Worker, or printed.

The Web sign-in adapter uses email-code verification followed by a server-issued Firebase custom token and the existing `/api/auth/firebase/session` exchange over HTTPS/same-origin. Phone is the preferred, unverified login alias; email is also accepted, and both aliases resolve to the same server account. Challenges for phone sign-in are sent only to the stored verified email. No SMS, Phone OTP, reCAPTCHA or second account password is used. One verified Firebase UID maps to one V3 account. V3 uses its own encrypted/authenticated Secure/HttpOnly/SameSite=Strict `__Host-` session cookie, not client-selected role claims.

Pilot email delivery is explicitly `V3__Auth__EmailDeliveryMode=Resend`, with the protected `V3__Auth__ResendApiKey` and fixed sender `Weymela Pilot <no-reply@pilot-mail.weymela.com>`. The adapter posts only to Resend's fixed HTTPS API with bounded timeouts and generic failures. It supports Signup, DeviceEnrollment/sign-in, and PinRecovery codes; it never sends a readiness email or places a code in a URL. Provider failure rolls back challenge creation/consumption.

Trusted roles are V3-local `IdentityBinding` + active `CommercePermission`, with safe `PublicWorkspaceProfile`. Ignore client role/business IDs. Bind exact provider/project/UID to one active workspace; ambiguous multiple memberships fail closed. Provision mapping only through reviewed Admin/bootstrap tooling under separate authorization, with audit/version and inactive/revocation tests. No public endpoint accepts arbitrary role grants. Cashier Business assignment is explicit; business actor maps to its own Business. Existing Firebase UID does not imply V3 authorization.

Required external inputs before live sign-in are: the four public Web build variables `VITE_FIREBASE_API_KEY`, `VITE_FIREBASE_AUTH_DOMAIN`, `VITE_FIREBASE_PROJECT_ID=weymela-pilot`, `VITE_FIREBASE_APP_ID`; API-only Resend settings; API-only read-only Firebase Admin JSON; base64 32+ byte `V3__Auth__CodeHashKey` and `V3__Auth__PinPepper`; and the existing external cookie certificate/key directory. Release evidence records the Web Firebase project, and Pilot preflight rejects a Web/API mismatch. The authorized V3 hostnames are already approved, but real email/Firebase calls and Pilot deployment still require a separately authorized external acceptance. Existing Android/OAuth identities remain untouched. No Firebase-user bulk import or V2 credential reuse.

| Setting/input | Classification | Scope |
|---|---|---|
| `V3__Auth__EmailDeliveryMode=Resend` | environment setting | API only |
| `V3__Auth__ResendApiKey` | secret | protected API env only |
| `V3__Auth__ResendFromAddress=no-reply@pilot-mail.weymela.com` | environment setting | API only |
| `V3__Auth__ResendFromName=Weymela Pilot` | environment setting | API only |
| `V3__Auth__FirebaseCustomTokenMode=FirebaseAdmin` | environment setting | API only |
| `V3__Auth__FirebaseProjectId=weymela-pilot` | public config | API/Worker; must match Web evidence |
| `GOOGLE_APPLICATION_CREDENTIALS=/run/secrets/v3-firebase-admin.json` | file path only | API only |
| `/etc/weymela/pilot/firebase-admin.json` | private read-only service-account file | mounted into API only |
| `V3__Auth__CodeHashKey` | base64 32+ byte secret | protected API env only |
| `V3__Auth__PinPepper` | base64 32+ byte secret | protected API env only |
| cookie PFX/password and persistent key directory | private file/secret/storage | API only |
| four `VITE_FIREBASE_*` values | public build config | Web build only |

Source validation cannot prove domain verification, API-key permissions, mounted-file ownership, or real provider delivery/signing. The separately authorized external acceptance must verify those without printing secrets, then execute one controlled signup/sign-in/recovery path before participant admission.

The additive `AddPhoneLoginAliases` migration adds the challenge's bound email/phone hashes and a server-only email delivery address. It does not change financial tables. After isolated migration, the existing API runtime grants on `AuthIdentifiers` and `EmailAuthChallenges` remain sufficient; no new Worker grant is required. Applying this migration to live Pilot requires the normal migration review and backup gate.

## Deposits: pending manual approval, never fake real money

Current Phase6 API supports `ManualApproval`: Business submits any positive amount with normalized external/proof **reference** → pending request → assigned Platform Admin verifies independent receipt → approve/reject → approval atomically credits Business wallet with journal/audit/idempotency. An external confirmation reference cannot credit twice. No Business-type wallet threshold, no self-credit, no bank automation. File-upload proof storage is not implemented; reference must point to operator-controlled evidence outside repository/logs.

Pilot configuration prepares ManualApproval but financial writes remain frozen. No end-to-end funding can be performed until explicitly authorized to enable controlled V3 test writes. Select one of: real receipt-based isolated Pilot accounting with reconciliation, or clearly marked non-real-money controlled test records approved by the owner. Never present fabricated Development deposits as bank receipts; do not mix either with V2 ledgers. Admin deposit-review API exists, but a complete dedicated review UI/operator workflow is a pre-participant gate. Define approvers, amount/reference verification, proof access, double approval policy if needed and reconciliation owner.

## Social: manual evidence fallback design (not implemented/enabled)

Live TikTok/YouTube/Instagram credentials are optional for Pilot. The approved alternative is Admin/manual **verification of real platform evidence**, not manual invention of counts:

1. Creator identifies approved content tied to the Creator participation. Authorized reviewer validates ownership/content/platform independently, records actual count, provider timestamp, content ID and immutable evidence reference/checksum.
2. Reviewer identity, review time, decision and evidence provenance are audited; evidence is append-only, idempotent and bound to Creator + Campaign + content. Creator/Business cannot author authoritative counts. A correction adds evidence; never rewrites history or resets baseline.
3. A reviewed `ISocialVerificationAdapter`/`IVerifiedViewProvider` implementation returns only approved fresh evidence matching all bindings/capabilities. Missing/stale/unreviewed evidence fails closed. Existing Phase6 router expects fresh provider evidence (<=15minutes), verified ownership/content, health and matching provider references; any manual timing policy change needs explicit review.
4. The existing Phase4 view reward transaction consumes only that Creator Budget, captures baseline once, rewards complete blocks, carries remainders, records anomalies and keeps immutable snapshot pricing. No direct wallet/earning SQL adjustment substitutes for a view reward.

Current runtime accepts only `Disabled` (Pilot) and Development-only `Test`; **there is no implemented `Manual` mode/Admin evidence endpoint**. Adding an enum/env string cannot make it work. Phase7 prepares this bounded design; implementation/security tests and possibly evidence persistence review need explicit follow-up approval. No migration is invented now. Until then sign-in/non-view commerce preparation can proceed, but verified-view acceptance is pending. Lack of live social API credentials is not itself the deployment blocker.

## Legal publication gate

Do not invent legal language or hard-code a time restriction. [`v3-pilot-legal-documents.template.json`](v3-pilot-legal-documents.template.json) is a **non-importable publication inventory**, not live DB records or accepted terms. Required owner-approved set: Terms of Service, Privacy Policy, Business Agreement, Creator Agreement, Anti-Circumvention Agreement. Record immutable version ID, exact content hash, effective date, approved content URL/reference and signer role. LegalAcceptance binds user/role to exact DocumentVersionId/time and permitted metadata, never blanket future acceptance.

Existing application gates enforce current Business/Creator Agreement + Anti-Circumvention on relevant actions. Terms/Privacy and complete content presentation must be reviewed/enforced before admitting live participants; current acceptance metadata alone is not proof the full text was shown. No automatic acceptance or data-only bypass. Internal test documents may be used only with explicit owner permission, conspicuous INTERNAL TEST / NOT FINAL LEGAL TERMS marking, new test version IDs and recorded tester consent. They must not be published to live participants or counted as final acceptance.

Customer activation now requires explicit account-level acceptance of the exact current Terms of Service and Privacy Policy IDs and content hashes. The Web presents stable links at `/legal/terms-of-service` and `/legal/privacy-policy`; owner-approved content must be published and verified at those paths before Pilot participants are admitted. The application does not invent or seed final legal prose. Pilot readiness remains fail-closed when current effective Terms/Privacy version records are missing, and publication/content/hash verification remains an operator acceptance action.

## Test identity plan — 10 distinct identities, no credentials in source

| Label | Role | Purpose |
|---|---|---|
| V3-ADMIN-01 | PlatformAdmin | Oversight,pricing,deposit approval,payouts,reconciliation |
| V3-BUSINESS-A | Business A owner | Own wallet/Campaign/funding/checkout |
| V3-BUSINESS-B | Business B owner | Cross-Business denial and independent funds |
| V3-CREATOR-01 | Creator | Category/region eligible;multiple Campaign earnings |
| V3-CREATOR-02 | Creator | Second isolated Creator Budget;top-up/exhaustion |
| V3-CREATOR-03 | Creator | Initially ineligible followers/category;denied discovery/join |
| V3-CUSTOMER-01 | Customer | Hybrid QR/cashback/payout carry-forward |
| V3-CUSTOMER-02 | Customer | QR/history ownership/IDOR tests |
| V3-CASHIER-A | Cashier assigned A | Correct QR/amount/checkout |
| V3-CASHIER-B | Cashier assigned B | Wrong-Business attempt then legitimate A retry |

Labels are not login credentials or seeded accounts. Operator retains actual existing-project UIDs and mappings outside Git; use separate tester-owned accounts as approved, MFA for Admin where supported, no shared secrets/passwords. Do not create Firebase users without authorization. Only public pseudonymous profile fields enter V3 fixtures; never copy live V2 phone/email/private addresses. Account activation and legal/privacy consent precede tests. Record role IDs/Budget ownership in restricted test evidence, not public screenshots. Two Cashiers concurrently attempting one Business's QR requires a separately approved second assignment/test session to that Business; do not grant cross-Business permissions just to pass a test.
