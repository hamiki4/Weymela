# Production registration and additional profiles

The verified Firebase binding and private email/phone identifiers belong to the
account. Customer, Creator and Business profiles share that identity. Creating a
profile never replaces a phone, enrolls a password, creates another Firebase user,
or credits a wallet. Creator activation still requires the existing Admin review;
Cashiers use Business-scoped invitations and Admins use privileged provisioning.

## API contracts

- `GET /api/onboarding/identity` returns only the authenticated account's identity
  and whether it already has profiles or enrollment requests.
- `POST /api/onboarding/registration` completes a verified identity's first profile.
- `POST /api/onboarding/profiles` adds a profile to an existing account and reuses
  server-owned identifiers. Client phone/password fields cannot change credentials.
- `POST /api/onboarding/profile` remains compatible with published Android
  versionCode 11. For an account with profiles it also ignores stale signup phones.

All three creation routes use the same serializable creation service, role checks,
consent validation and idempotency handling. A retry returns the existing active
profile or pending request. An inactive profile stays restricted. Concurrent
conflicts ask the caller to retry without creating another profile or wallet.

The Web Add Profile links explicitly select additional-profile setup. Saved signup
details are checked against the authenticated account and never automatically
submitted for another account. Additional-profile progress and request references
are stored per account in session storage. Policy confirmations are not retained
across refresh or a published-version change.

## Actual Production policy publication

The repository's `docs/legal/production/*OWNER-LEGAL-DRAFT*` files are incomplete
review drafts. This change does not approve or publish them. Production must not
use Pilot text or create acceptance records from an earlier generic checkbox.

Prepare a protected approved bundle containing:

1. `TermsOfService-en.txt`, `TermsOfService-am.txt`, `PrivacyPolicy-en.txt`, and
   `PrivacyPolicy-am.txt`, containing the exact approved UTF-8 bytes.
2. `publication.json`, following the existing `AccountLegalDocumentSource` contract:
   `environment: Production`, actual `productionApproved` and `attorneyReviewed`
   decisions, actual `ownerApprovalReference` and `legalApprovalReference`, and
   exactly four document entries. Each entry contains `type`, `language`,
   `approvedFinalVersion` ending in `-en` or `-am`,
   `approvedFinalContentSha256` prefixed with `sha256:`, and the actual approved
   `effectiveFromUtc` timestamp.

Run `python3 tools/ops/prepare-account-policy-publication.py --approved-bundle
<protected-bundle> --operator-user-id <actual-active-platform-admin-id>
--output-sql <new-protected-output.sql>` and review the resulting append-only SQL.
The tool does not connect to PostgreSQL, approve content, publish anything, record
user consent, or change runtime flags. The SQL rejects the wrong database,
inactive/non-Admin operators, version rewrites and superseded documents. It adds
four immutable versions and one idempotent publication audit event.

Using the protected Production operator credentials, apply the reviewed SQL and
mount the same bundle read-only at the configured approved-content directory.
Verify both policies in both languages through
`/api/legal/account/{terms|privacy}?language={en|am}` and check exact hashes against
PostgreSQL before switching the existing authorized consent mode to `Required`.
Do not change consent mode until actual approved content is available.

Consent binds the displayed language's exact current document IDs and hashes.
Missing or changed approved bytes block profile activation with an actionable
message and preserve verified identity and profile progress. No approval or consent
is inferred from a draft, deployment authorization, or a previous generic checkbox.

## Deployment boundary

There is no schema or financial configuration change. Deploy only the checked API
and Web correction after protected-main CI. Preserve the current Worker, deposit
activation and storage, financial history, Pilot, V1 rollback, and signing files.
Use synthetic identities for live smoke tests and retain an accurate distinction
between live email submission, actual mailbox verification, and isolated tests.
