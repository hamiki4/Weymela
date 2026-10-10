# Weymela V3 Production replacement preparation

Preparation only. No Production services, databases, Android releases, or Pilot settings were changed.

## Confirmed release inputs and blockers

- Production currently uses the legacy `creatorpay-prod` Compose project and V1.0.1 API/Worker/Web images. Its PostgreSQL service has an existing persistent data volume. Keep that database and its release configuration intact as the rollback source.
- The V3 EF migration chain starts with `20260911225904_InitialV3Schema` and the Production runtime guard accepts only a database name beginning `weymela_v3_prod`. The V3 initial migration creates the `v3` schema. No legacy V1-to-V3 financial or identity data migration was found. The live Production migration history was not queried; this environment has no `psql` client or V3 Production database credentials.
- V3 Web requests use same-origin `/api` through its Web proxy. Mobile clients must use `https://api.weymela.com`. The immutable release workflow supports Production Web Firebase settings only when explicitly dispatched with `firebase_target=production`; it checks the project ID is `weymela-production`.
- Repointing `api.weymela.com` can affect already-installed V1 Android clients while a Play update is in staged rollout. Preserve V1 API compatibility for that window or approve and test a forced-update/versioning strategy first. The V1 Android source and its route contract were not present for comparison.
- This checkout contains no Android/Capacitor project, Gradle files, Android manifest, Production `google-services.json`, or AAB. `/opt/CreatorPayV2` is unavailable in this environment. No Android package, deep-link, permissions, notification, or authentication implementation can be validated here.
- The existing `/etc/creatorpay/android-upload/weymela-upload.jks` file is a PKCS#12 container despite its suffix. Its protected password opens it; the certificate alias is `weymela-upload`, the certificate public key matches the single contained private-key entry, and the certificate uses RSA 4096, above Google Play's documented RSA 2048 minimum for upload keys ([Play App Signing requirements](https://support.google.com/googleplay/android-developer/answer/9842756?hl=en)). Gradle signing must specify the PKCS#12 store type. The key-bag friendly name could not be independently confirmed without Android `keytool`; validate the alias in a controlled signing build. The private key was not exported or printed.
- The upload certificate SHA-256 fingerprint and local key identity still need comparison against the upload certificate registered for `com.weymela.app` in Play Console. Play Console access and the highest accepted/used version code across tracks were unavailable, so do not select a version code or produce a signed AAB yet.

## Separate V3 database and cutover sequence

1. Preserve the current V1.0.1 images, Compose files, legacy PostgreSQL volume, media, configuration, and rollback records. Produce an encrypted off-host backup and complete a restore rehearsal before setting a cutover date.
2. Provision a separate Production V3 PostgreSQL database whose name starts `weymela_v3_prod`, separate volumes and media paths, an API runtime role, and a distinct least-privilege `weymela_v3_migrator` role. Do not attach the legacy V1 database or reuse its credentials.
3. Review the exact approved V3 migration bundle and apply it only to the new database through the fail-closed `tools/ops/run-v3-migrations.py` wrapper after the target identity and password-file authentication pass in an isolated rehearsal. Verify schema, grants, constraints, and migration history.
4. Before importing any Production data, approve a field-level mapping for users and role enrollments, wallets, reservations, deposits and receipts, payouts, external identities, and audit history. Reconcile opening balances and liabilities against the legacy ledger and bank records. Require duplicate-journal and negative-balance checks. Do not discard records or infer that V3's baseline is a data migration.
5. Rehearse any approved data transfer against a restored, isolated snapshot. Record checksums, row counts, financial totals, exceptions, and sign-off. Keep real deposits and payouts disabled until independent operational reconciliation is approved.
6. At the eventual cutover, freeze legacy writers for a consistent final snapshot, apply only the reviewed delta/import, validate authentication and read paths, then switch Web/API routing. Preserve the old stack and database for the approved rollback window. Roll back routing and images if validation fails; never down-migrate or overwrite the legacy database.

## Existing Android update gates

- Obtain the existing Android source/build checkout and Production `google-services.json` for the registered `com.weymela.app` Firebase Android client. Confirm `project_id=weymela-production` and that a client entry matches `com.weymela.app`; keep Pilot Firebase configuration out of the Production build.
- Keep `applicationId` exactly `com.weymela.app`, use the existing upload keystore and alias, and configure the keystore as PKCS#12. Compare its certificate fingerprint with Play Console before signing. Read the latest accepted/used version code from Play Console and select a higher unused code; historical versionCode 2 is not sufficient evidence.
- Verify the manifest, auth/session flow, production API origin, verified deep links, notification/Firebase setup, and release permissions from the existing project. As of 2026-08-31, Google Play requires new app updates to target Android 16/API 36 or higher; validate [current policy](https://support.google.com/googleplay/android-developer/answer/11926878?hl=en) again when submitting.
- Build and inspect the unsigned/release bundle only after the correct source, Production config, and build tools are available. Sign it in the authorized environment that can read the existing key without exporting it. Do not upload it until the owner approves the Play Console release.

## Required approvals before release

- Complete the Production Firebase service-account validity/IAM check and provide the registered Web and Android client configuration through approved public build settings.
- Bind the existing `Weymela-Production` Resend key through the protected API-only secret reference; validate the proposed `no-reply@mail.weymela.com` sender.
- Provision independent Production auth-code and PIN secrets, the cookie certificate/password, and durable data-protection key storage through the approved secret mechanism.
- Confirm the existing Play upload certificate and maximum version code in Play Console; provide the Android source and Production Firebase client file.
- Approve the legacy-data mapping, financial reconciliation, backup/restore rehearsal, and rollback window. Restore authorized GitHub access before pushing the branch, opening a PR, and running hosted CI.
