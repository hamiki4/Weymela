# V3 Pilot runtime secret and environment hardening

This procedure is an operator gate. It does not authorize deployment, database
changes, provider calls, or Production configuration.

## Protected inputs

The API environment assembler accepts two owner-only, regular, non-symlink files:

- `/etc/weymela-v3/pilot/api.env`: complete operational input using only the
  documented base keys.
- `/opt/weymela/secrets/v3-pilot-auth.env`: authentication input using only the
  documented authentication keys.

The files are parsed as data and are never sourced by a shell. Cross-file
duplicates are rejected except `V3__Auth__FirebaseProjectId`, which must occur in
both files with the exact value `weymela-pilot`. The complete output is written
atomically to `/etc/weymela-v3/pilot/api.env` as root-owned mode `0600`.

Before using the assembler, an operator must privately confirm that the existing
operational file contains no authentication-only keys. If it does, create a
root-only operational copy containing exactly the approved base keys; do not
remove operational settings merely to make validation pass.

Validate without writing:

```sh
sudo python3 tools/ops/pilot-runtime.py assemble-api-env \
  --base /etc/weymela-v3/pilot/api.env \
  --auth /opt/weymela/secrets/v3-pilot-auth.env \
  --output /etc/weymela-v3/pilot/api.env \
  --check-only
```

After review and a protected backup/checkpoint, omit `--check-only` to perform the
atomic replacement. The command reports key names and status only.

## Cryptographic values

`V3__Auth__CodeHashKey` and `V3__Auth__PinPepper` must be independently generated
with an approved cryptographic random generator and stored directly in protected
configuration without displaying them in a terminal, command history, ticket, or
log. Each value must be canonical base64 representing at least 32 bytes with at
least 16 distinct byte values. They must not be equal or derived from one another.

Do not copy repository fixtures or placeholder strings. The Resend key, database
password, and certificate password also remain external to Git. Environment-file
values must avoid shell interpolation/control characters; use an operator-approved
base64/base64url password format where necessary.

## Host metadata contract

The API image runs as UID/GID `1654:1654`. Before any Pilot start, an operator must
eventually establish:

| Host path | Type | Owner | Mode | Container use |
|---|---|---:|---:|---|
| `/opt/weymela/secrets/v3-firebase-admin.json` | regular, non-symlink | `1654:1654` | `0400` | API secret, read-only |
| `/etc/weymela-v3/pilot/cookie-protection.pfx` | regular, non-symlink | `1654:1654` | `0400` | API secret, read-only |
| `/var/lib/weymela-v3/pilot/cookie-keys` | persistent directory | `1654:1654` | `0700` | API bind mount, writable |

The eventual privileged actions are limited to owner/mode correction on those
exact paths, for example `chown 1654:1654` followed by `chmod 0400` for each file,
and `install -d -o 1654 -g 1654 -m 0700` for the key directory. Review the resolved
paths before executing any such command. Never use a recursive broad target.

The metadata-only check does not open or hash protected files and never changes
permissions:

```sh
python3 tools/ops/pilot-runtime.py validate-host-metadata
```

## Data Protection restart acceptance

After immutable images and reviewed mounts exist, run the API image with its normal
Pilot environment and secrets but without dependencies or HTTP startup:

```sh
dotnet Weymela.Api.dll --probe-data-protection-restart
```

The probe requires the approved Pilot environment/project, loads the configured
PFX, verifies a current private key, verifies key-directory writability, protects
a fixed non-secret sentinel, persists only its protected form as mode `0600`,
disposes the provider, creates an independent provider over the same keyring, and
unprotects the sentinel. Run it again after an API/application restart; the later
invocation must decrypt the existing protected sentinel. It prints only pass/fail
status. It does not connect to PostgreSQL, call Resend/Firebase, or start HTTP.

Failure is a deployment stop condition. Do not delete an existing keyring to make
the probe pass; investigate certificate password, certificate validity, ownership,
mounts, and persistence privately.

## Compose isolation

The API alone receives the Firebase Admin JSON, cookie PFX, and writable persistent
key directory. Worker and Web receive none of them. The API environment must retain
`V3__FinancialWritesEnabled=false`; the assembler, runtime loader, Compose preflight,
and tests all reject an attempted unfreeze.
