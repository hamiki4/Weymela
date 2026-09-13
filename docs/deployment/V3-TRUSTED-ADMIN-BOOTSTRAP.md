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
database guard. No Admin has been provisioned in the Pilot database.
