# Trusted V3 Platform Admin bootstrap

This operator-only procedure is preparation guidance. It is not an HTTP endpoint or Web UI operation. The application source contains a fixed allowlist for the explicit target aliases `pilot`, `production-test`, and `production`; the protected operations register holds the environment-specific database, bootstrap role, and Firebase project mapping. Do not publish that private register or copy its credentials into this repository.

## Preconditions

- Use an approved, isolated V3 administration context and its protected bootstrap credential store.
- Select the target explicitly. There is no default target.
- Keep the six target database/role identifiers in protected operator configuration under `V3_BOOTSTRAP_PILOT_DATABASE`, `V3_BOOTSTRAP_PILOT_ROLE`, `V3_BOOTSTRAP_PRODUCTION_TEST_DATABASE`, `V3_BOOTSTRAP_PRODUCTION_TEST_ROLE`, `V3_BOOTSTRAP_PRODUCTION_DATABASE`, and `V3_BOOTSTRAP_PRODUCTION_ROLE`. These values are not stored in the public source repository.
- The tool rejects names outside the V3 database/bootstrap-role naming boundary and rejects duplicate database or role assignments across the three targets.
- The bootstrap tool checks the supplied database and role before connecting, then verifies the connected server’s current database and role before it can write.
- For `production-test`, use only the disposable test database and test credentials. Never use a live server or a Pilot/live Production database.
- For `production`, obtain the owner’s explicit authorization reference and confirm the destination is the separate V3 Production database, never the V1 database.
- Never put database passwords, Firebase service-account contents, tokens, PINs, or other credentials in arguments, source control, or logs. Use the approved protected secret/file mechanism.

## First Platform Admin

1. Create or identify the owner’s existing V3-local user through the normal authenticated identity flow. The bootstrap does not create a user or Firebase identity.
2. Confirm one active Firebase binding maps the V3-local user to the owner’s actual, enabled Firebase UID in the project selected for that explicit target. The Production target verifies the UID with Firebase Admin before opening its write transaction.
3. Confirm no active V3 Platform Admin already exists. Do not copy a Pilot identity or automatically bind a legacy V1 Admin.
4. Run the trusted console operation with `--target`, `--firebase-project`, `--firebase-uid`, `--user-id`, `--valid-after`, `--operator-user-id`, `--operator-reference`, `--correlation-id`, and a unique `--idempotency-key`. Production also requires `--production-authorization-reference`.
5. The operation uses a serializable transaction and creates only the Platform Admin permission, idempotency record, and audit event against the pre-existing identity binding. Replaying an identical request is idempotent; conflicting mappings or permissions fail without partial writes. First-admin bootstrap closes once one trusted Platform Admin exists.

The operator reference and Production authorization reference are audit identifiers, not credential fields. Use bounded, non-secret approval identifiers only.

## First financial configuration

After a trusted V3 Platform Admin exists, the console supports a separate `financial-configuration-v1` operation. It requires the existing Admin identity and explicit, owner-approved values for both view modes, verified-sale rates, payout thresholds, effective UTC time, and the Production UGC, audience, and duration fields. Production rejects omitted UGC values rather than applying schema defaults. Optional minimum Promotion budgets may be null only when the owner expressly approves that choice.

The operation is serializable, audited, and idempotent. It creates only the first pricing root, version, idempotency record, and audit event. It refuses to overwrite existing configuration and does not create wallets, Promotions, payouts, or ledger transactions. Normal Admin financial settings remain the subsequent versioning path. The non-importable Production worksheet records known candidate rules and every unresolved owner value; it is not a runtime configuration.

Keep financial writes disabled, deposits disabled, and external payouts disabled until the separate owner activation process is approved. Never use a bootstrap command to enable them.

## Legal policies

English and Amharic Production Terms and Privacy drafts are review material only. Their draft versions and content hashes are recorded in `docs/legal/production/approval-plan.json`; they are not approved policy records and cannot be imported or activated. Do not seed test policies into live Production. Legal approval, owner approval, final versioning, and a separately reviewed publication process are required before readiness can advance.

## Audit and cleanup

Preserve the tool’s audit and idempotency records. Keep the approval decision outside credential fields and avoid including personal or secret data in references. Remove disposable test credentials and resources after testing using their approved cleanup procedure; retain only the evidence required by the applicable retention policy.
