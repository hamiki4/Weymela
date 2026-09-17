# V3 authentication to V2-derived product integration

Phase I1 keeps V3 as the only identity and authentication authority. The browser transfers only a short-lived opaque authorization code. The V2-derived API redeems that code directly with the V3 API, creates its own private application session, and periodically revalidates V3 authority. The two applications do not share cookies, Data Protection keys, passwords, PINs, Firebase credentials, or long-lived browser tokens.

## Isolated Pilot configuration contract

The integration remains disabled by default. A separately authorized integration Pilot must provide protected runtime configuration; no values are committed.

V3 API requires `V3__ProductIntegration__Enabled`, `Issuer`, `Audience`, `Environment`, `CallbackId`, `CallbackUrl`, `BeginUrl`, `ProductWebUrl`, `ClientId`, and `ClientSecret`. `Environment` must exactly equal the V3 runtime environment. Begin and callback are fixed HTTPS V2-derived API endpoints. ProductWebUrl is the fixed HTTPS V2-derived Web origin. ClientSecret is a separate 32-byte-or-stronger secret.

The V2-derived API requires matching `V3Integration__Issuer`, `Audience`, `Environment`, `CallbackId`, `ClientId`, and `ClientSecret`, plus fixed HTTPS `V3ApiUrl`, `V3WebUrl`, and `ProductWebUrl`. `PlatformAdminUserAccountId` is the explicitly reviewed V2 Admin principal to bind to the existing V3 Platform Admin; it must not be inferred from email or phone. CORS must allow only the V2-derived Web origin. V3 server endpoints must be reachable only over the intended protected network/TLS path.

Issuer, audience, environment, callback ID, and all three origins must match exactly. Use distinct credentials per environment. Never put the integration secret in Web configuration, URLs, logs, release artifacts, or Git.

## Runtime and migration contract

Required runtime components are the V3 API and Web, the V2-derived API and Web, and their existing isolated PostgreSQL databases. Existing workers do not participate in handoff redemption and require replacement only when their normal immutable release compatibility policy requires it.

Apply only the reviewed V3 `AddProductHandoffTransactions` migration and V2 `AddV3ExternalIdentityIntegration` migration to disposable rehearsal databases first. The V3 API receives least-privilege access to the private handoff table; the V3 Worker remains denied. Do not point an integration Pilot at V2 Production or the current V3 Pilot database.

## Rollback prerequisites

Before deployment, capture both deployment-reference configurations, immutable image digests, migration histories, and verified backups of both isolated databases. The migrations are additive and previous application images ignore their new tables/column, but compatibility must still be rehearsed against exact release images. Prefer application-image rollback with the additive schema retained. Do not run destructive down-migrations or restore a live database without separate authorization. Revoking the integration client secret and setting both integration enable flags false terminates new handoffs; existing V2-derived external sessions must also be revoked or allowed to expire under the reviewed incident procedure.

No Phase I1 source action deploys, migrates, changes DNS/reverse proxy, enables financial writes, or changes Production.
