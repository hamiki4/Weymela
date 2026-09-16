# V3 Pilot draft legal publication

Phase 4A.1 provides concise Pilot/Draft Terms of Service and Privacy Policy for the isolated `weymela_v3_pilot` onboarding lifecycle. They are not attorney-reviewed final Production documents and are not authorized for Production.

The immutable API image contains `/app/PilotLegal/publication.json`, `/app/PilotLegal/publish.sql`, and the exact document bytes under `/app/PilotLegal/documents/`. The immutable Web image serves those reviewed document bytes and the readable routes referenced by the V3 legal-status contract.

Publication is deliberately not automatic. A separate Pilot deployment authorization must:

1. verify the approved API and Web image digests and extract the publication bundle from the approved API image;
2. recompute each document SHA-256 and compare it to `publication.json`;
3. verify the target is exactly `weymela_v3_pilot`, preserve a verified backup, and use the protected migration identity;
4. execute `publish.sql` with `psql -X` and stop on any error;
5. verify exactly one matching Terms row and one matching Privacy row, then test version-bound Customer acceptance.

The SQL is target-guarded, transaction-scoped, conflict-detecting, and idempotent. It inserts only two `LegalDocumentVersions` rows. It does not create acceptances, profiles, users, financial configuration, or Production data.
