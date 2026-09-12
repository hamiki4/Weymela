# Weymela V3

Weymela V3 is a new platform/middleman implementation. It shares no V2 migrations, entities, secrets, databases, or deployment state.

## Status

Phases 0–6 provide architecture, domain/application foundations, isolated PostgreSQL persistence, the verified-view/QR financial engine, role-specific responsive Web/PWA and security/operational readiness. See [persistence design](docs/architecture/PERSISTENCE.md), [financial engine](docs/finance/PHASE-4-FINANCIAL-ENGINE.md), [Web/API design](docs/architecture/PHASE-5-WEB-API.md), and [Phase 6 readiness](docs/deployment/PHASE-6-READINESS.md). Phase7 now prepares [side-by-side Pilot CI/runtime/runbooks](docs/deployment/V3-PILOT-PREPARATION.md), without changing product implementation or deploying. Capacity, live sign-in/provisioning, manual evidence, legal and operational gates remain; no push or public cutover is authorized by these files.

## Projects

The solution is organized into Domain, Application, Infrastructure, API, Worker, and Web projects with matching test boundaries. PostgreSQL is the planned persistence system; Firebase remains an integration seam only.

## Local development and acceptance

Use .NET 10, Node 24+, and a local Docker runtime for **disposable PostgreSQL tests only**. Never point tests or development personas at an existing Pilot/Production database. Do not build Docker images on the server.

```sh
dotnet build Weymela.slnx -c Release
dotnet test tests/Weymela.Domain.Tests -c Release
dotnet test tests/Weymela.Application.Tests -c Release
dotnet test tests/Weymela.Infrastructure.Tests -c Release
dotnet test tests/Weymela.Api.IntegrationTests -c Release
cd src/Weymela.Web
npm ci
npm test
npm run build
```

For browser acceptance, from the repository root run `V3_SOURCE_ROOT="$PWD" dotnet run --project tests/Weymela.BrowserHost -c Release`. It creates only its own disposable test database and loopback API, then writes a mode-0600 `.artifacts/browser-host.json` control file containing its ephemeral URL/access code. In a second terminal run `npm run e2e` from `src/Weymela.Web`. Install matching Playwright Chromium first if it is not already available. Stop the test host with Ctrl+C afterward; it disposes its PostgreSQL container. Do not copy its access code into source, logs or a deployed environment.

The production frontend can be served on the API's same origin by explicitly configuring `V3:WebRoot`; development Vite proxies `/api` to `WEYMELA_V3_API`. API startup never creates a database or runs fixture seed. Test sign-in is opt-in and impossible outside Development. Firebase-compatible server verification and manual deposit approval boundaries are implemented, but require explicit trusted provisioning/configuration; real client sign-in and social/payment/push integrations remain deferred. Outside Development, startup validates external configuration and financial writes default paused. Worker processes the durable inbox/outbox and observes QR expiry without financial posting.
