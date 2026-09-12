# Pilot adapters, legal publication and test identities

Preparation only: no Firebase call/configuration/resource change, user provisioning, legal publication, fabricated activity or financial posting was performed.

## Firebase: reuse existing identity, do not create a new project

Owner must provide the exact approved existing Firebase **project ID** through protected deployment configuration `V3__Auth__FirebaseProjectId`. It is a public identifier, not a secret; no value is guessed from a domain and no current V2 secret file is copied. Expected issuer `https://securetoken.google.com/<project-id>`, audience `<project-id>`, RS256, valid expiry/subject and recently authenticated ID token. Current verifier obtains Google's public X.509 signing certificates from the fixed HTTPS endpoint and validates exact project identity. No service-account private key or Firebase Admin credential is needed for this verification path.

The Web sign-in adapter still needs integration. It must obtain an existing-project Firebase ID token and exchange it at `/api/auth/firebase/session` over HTTPS/same-origin, never persist tokens in application logs or URLs. V3 uses its own encrypted/authenticated Secure/HttpOnly/SameSite=Strict `__Host-` session cookie, not client-selected role claims. Current CSP is self-only; when an SDK/redirect provider is selected, review its minimum required connections/frames against Firebase's official requirements, add only justified exact sources and test CSRF/redirect behavior. Do not silently loosen CSP or enable existing Production domains for V3.

Trusted roles are V3-local `IdentityBinding` + active `CommercePermission`, with safe `PublicWorkspaceProfile`. Ignore client role/business IDs. Bind exact provider/project/UID to one active workspace; ambiguous multiple memberships fail closed. Provision mapping only through reviewed Admin/bootstrap tooling under separate authorization, with audit/version and inactive/revocation tests. No public endpoint accepts arbitrary role grants. Cashier Business assignment is explicit; business actor maps to its own Business. Existing Firebase UID does not imply V3 authorization.

Required owner choices before sign-in: project ID, login providers (password/redirect), Firebase public client configuration, permitted V3 authorized domains/redirect URIs, trusted provisioning authority and role-sync lifecycle. Proposed new hosts require a future authorized Firebase domain change if the selected provider needs it; none is performed now. Existing Android package/signing/OAuth identities remain untouched. V3-local binding revoke is immediate; global Firebase revocation policy and session lifetime (current <=1hour/token expiry) require owner approval. No Firebase-user bulk import or V2 credential reuse.

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
