# Trusted Platform Admin bootstrap

This operation is preparation-only until the owner supplies a Firebase UID and V3-local
user ID. It is a manually executed console tool, never a public API or Web UI operation.

## Preconditions

- Run only from a trusted V3 administration context.
- Supply `V3_BOOTSTRAP_CONNECTION` from the protected V3 bootstrap secret store.
- The target guard must report `current_database() = weymela_v3_pilot` and
  `current_user = weymela_v3_bootstrap`.
- Confirm the owner-approved Firebase project is `weymela-pilot`.
- Do not pass tokens, passwords, or service-account material as arguments.

## Prepared command

```text
V3_BOOTSTRAP_CONNECTION=<protected-external-value> dotnet run \
  --project src/Weymela.Bootstrap/Weymela.Bootstrap.csproj -- \
  --firebase-project weymela-pilot \
  --firebase-uid <owner-approved-firebase-uid> \
  --user-id <v3-user-guid> \
  --valid-after <utc-rfc3339> \
  --operator-user-id <trusted-operator-guid> \
  --operator-reference <approved-reference> \
  --correlation-id <correlation-guid> \
  --idempotency-key <unique-bootstrap-key>
```

The tool uses a serializable transaction and creates an active Firebase `IdentityBinding`,
an active `CommercePermission` with `PlatformAdmin`, one idempotency record, and one audit
event. Replaying the same key and fingerprint returns the existing binding without duplicates.
Conflicting UID/user mappings or an incompatible permission fail without partial writes.

The current schema's unique identity indexes and permission primary key provide an additional
database guard. Once the trusted Platform Admin exists, first-admin bootstrap is closed.

## First financial configuration

After the trusted Platform Admin exists, the same protected console tool supports the separate
`financial-configuration-v1` operation. It is the only bootstrap path for the missing
`PlatformPricing` root and Version 1. It requires the existing Pilot Platform Admin identity,
the protected bootstrap connection and explicit owner-approved values for both Promotion view
modes, verified-sale percentages, payout thresholds and an effective UTC instant.

The operation is serializable, audited and idempotent. It uses the same configuration factory as
normal Platform Admin Financial Settings, creates only the root, Version 1, idempotency and audit
records, and refuses to run if any financial configuration already exists. It does not create or
modify wallets, Promotions, payouts or transactions and does not enable financial writes. After
Version 1, the Admin Financial Settings UI/API remains the only normal versioning path.

Supply all values as arguments; no financial value is hard-coded by the tool. Use
`--operation financial-configuration-v1`, the existing Platform Admin user ID, an explicit UTC
`--effective-from`, bounded operator reference/idempotency key and correlation ID. Optional
minimum Promotion budgets are omitted when the owner approves `null`. Rehearse the exact command
against a restored isolated database before Pilot use.
