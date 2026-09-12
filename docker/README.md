# V3 container preparation — no deployment

Phase7 supplies API/Worker/Web multi-stage Dockerfiles and an isolated image-only Pilot Compose definition. Build only on hosted CI/external builders,never on this application server. Current product source is unchanged.

Use [Pilot preparation](../docs/deployment/V3-PILOT-PREPARATION.md), [CI/registry instructions](../docs/deployment/GITHUB-ACTIONS.md), [capacity blocker](../docs/deployment/V3-PILOT-CAPACITY-AND-CLEANUP.md) and [database/backup runbook](../docs/deployment/V3-PILOT-DATABASE.md). Templates intentionally fail without explicit digests/secrets/TLS confirmation; they are not runnable credentials.

`compose.v3-pilot.yml` has no builds,no V2 networks,no public DB/API ports,financial writes forced off,health/resource/log limits and no automatic restart during preparation. Web is loopback18080 with same-origin `/api`; no direct cross-origin token/cookie redesign. Runtime/source caches,evidence and secrets are excluded by `.dockerignore`. Production deployment/cutover is not provided.
