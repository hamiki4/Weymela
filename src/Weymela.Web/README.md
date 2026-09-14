# Weymela.Web

Phase 5 React/TypeScript role workspaces. Read the authoritative [UX standards](../../docs/product/UX-STANDARDS.md) and [Web/API architecture](../../docs/architecture/PHASE-5-WEB-API.md).

Requires Node 24. Run `npm ci`, `npm test`, and `npm run build` here. Component tests are colocated in `tests/`; rendered acceptance lives in `e2e/`. The original root test directories remain reserved, not duplicate test engines.

Production Firebase sign-in uses only the public build variables `VITE_FIREBASE_API_KEY`,
`VITE_FIREBASE_AUTH_DOMAIN`, `VITE_FIREBASE_PROJECT_ID`, and `VITE_FIREBASE_APP_ID`.
The Pilot release build requires all four and requires `VITE_FIREBASE_PROJECT_ID=weymela-pilot`;
the API-side Firebase Admin credential and Resend key must never enter the Web build context.
Signup and recovery use a one-time code sent only to the registered email; phone is the
preferred unverified login identifier and email is also accepted. The server issues a Firebase custom token only after
verification, then the adapter exchanges a fresh ID token for the Weymela session. SMS,
Firebase Phone OTP, and a second account password are not used. No service-account
credentials are embedded in the Web build. Existing authorized-device, PIN lock/unlock,
and verified-email PIN recovery continue to use server-side state and HttpOnly credentials.

For actual HTTP/PostgreSQL browser acceptance, use the isolated `Weymela.BrowserHost` procedure in the [repository README](../../README.md), then run `npm run e2e` here. The browser host serves the production build and its own disposable test database. It never connects to an existing Pilot or Production database. Its email-code and Firebase verifier adapters are disposable test boundaries only; they expose no code/token route outside that host and are never registered by Pilot/Production. `e2e/auth-onboarding.spec.ts` covers email signup, phone-alias sign-in, and independent Customer/Creator/Business approval and profile switching through the real API.

Test sign-in, public profiles, view verification and development-only deposit credit use explicit development adapters. They are not production integrations. No V2 source, secrets or configuration is copied here.
