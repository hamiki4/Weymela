# V3 container preparation — no deployment

Phase7 supplies API/Worker/Web multi-stage Dockerfiles and an isolated image-only Pilot Compose definition. Build only on hosted CI/external builders,never on this application server.

Use [Pilot preparation](../docs/deployment/V3-PILOT-PREPARATION.md), [CI/registry instructions](../docs/deployment/GITHUB-ACTIONS.md), [capacity blocker](../docs/deployment/V3-PILOT-CAPACITY-AND-CLEANUP.md) and [database/backup runbook](../docs/deployment/V3-PILOT-DATABASE.md). Templates intentionally fail without explicit digests/secrets/TLS confirmation; they are not runnable credentials.

`compose.v3-pilot.yml` has no builds,no V2 networks,no public DB/API ports,financial writes forced off,health/resource/log limits and no automatic restart during preparation. Web is loopback18080 with same-origin `/api`; no direct cross-origin token/cookie redesign. Runtime/source caches,evidence and secrets are excluded by `.dockerignore`. Production deployment/cutover is not provided.

The Pilot API alone receives `V3__Auth__ResendApiKey` from its protected env file and the read-only `v3-firebase-admin.json` secret at `/run/secrets/v3-firebase-admin.json`. Worker and Web receive neither. The Web image is built with only the public `VITE_FIREBASE_API_KEY`, `VITE_FIREBASE_AUTH_DOMAIN`, `VITE_FIREBASE_PROJECT_ID`, and `VITE_FIREBASE_APP_ID`; CI requires project `weymela-pilot` and records it in immutable release metadata. `pilot-preflight.py` rejects disabled adapters, malformed cryptographic configuration, missing API secret mount, or a Web/API project mismatch without printing expanded configuration.
