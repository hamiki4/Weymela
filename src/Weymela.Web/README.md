# Weymela.Web

Phase 5 React/TypeScript role workspaces. Read the authoritative [UX standards](../../docs/product/UX-STANDARDS.md) and [Web/API architecture](../../docs/architecture/PHASE-5-WEB-API.md).

Requires Node 24. Run `npm ci`, `npm test`, and `npm run build` here. Component tests are colocated in `tests/`; rendered acceptance lives in `e2e/`. The original root test directories remain reserved, not duplicate test engines.

For actual HTTP/PostgreSQL browser acceptance, use the isolated `Weymela.BrowserHost` procedure in the [repository README](../../README.md), then run `npm run e2e` here. The browser host serves the production build and its own disposable test database. It never connects to an existing Pilot or Production database.

Test sign-in, public profiles, view verification and development-only deposit credit use explicit development adapters. They are not production integrations. No V2 source, secrets or configuration is copied here.
