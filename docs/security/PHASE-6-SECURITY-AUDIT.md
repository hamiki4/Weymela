# Phase 6 security boundary

Scope: clean V3 only. No live Firebase, V2, existing Pilot/Production, DNS, TLS, signing keys, deployment or image build was used for acceptance. Automated HTTP tests use TestServer; PostgreSQL tests own temporary `v3_test_*` databases.

## Authentication and session transport

The live boundary exchanges a recently authenticated Firebase ID token for an application-owned cookie. It is **not** a Firebase Admin session cookie. RS256 signature, Google public key ID, exact project issuer/audience, subject, expiry, issued-at and recent `auth_time` are verified. Tokens and provider errors never enter persisted results or logs. Public certificates are fetched on demand from the fixed Google endpoint with bounded responses, caching and unknown-key throttling. Tests use locally generated RSA keys; no live project was contacted.

The verified external subject maps to one active `IdentityBinding` and one stable V3 user. That account may have multiple independently active `CommercePermission` memberships. JWT role claims and payload-selected roles are not authorization inputs: a selected profile is resolved server-side against the account's active memberships. No public identity-binding or role-grant endpoint exists. Provisioning/revocation is a separately authorized administrative prerequisite; multi-profile sign-in requires an explicit profile selection and never silently chooses a role.

Pilot/Production use `__Host-WeymelaV3.Session`, Secure, HttpOnly, Path=/, SameSite=Strict, one-hour maximum, no sliding renewal. Data-protection keys are persisted externally and encrypted with an external private certificate; the application discriminator includes environment and Firebase project. Every protected request rechecks active binding/version/valid-after and current role/workspace/Business assignment. Local revocation is immediate. Firebase-global revocation is not polled: an existing app session lasts at most its pinned expiry unless its local binding is revoked. Owner approval of this session/revocation policy is a live-enablement prerequisite.

The Firebase Web adapter obtains a fresh ID token after server-side email-code verification/custom-token sign-in and POSTs it to `/api/auth/firebase/session`; it does not persist bearer tokens in localStorage or send role/Business selectors. Phone is an unverified identifier only; no SMS/Phone OTP flow exists. Live use still requires protected public configuration, email delivery/signing and trusted V3 identity provisioning.

CSRF protection is explicit-origin + `X-Weymela-Request: 1` on mutations, SameSite Strict cookie, and CORS restricted to configured origins. The current browser uses same-origin `/api`; a future separate-origin topology needs explicit browser configuration and compatible same-site cookie routing, not wildcard CORS or a silent token-model switch. Unknown JSON members are rejected. No cookie/bearer ambiguity or cookie-auth bypass endpoint exists.

## Endpoint audit matrix

All `/api` routes except auth mode, identity exchange and explicit Development sign-in require an active authorized workspace. Anonymous health exposes only status.

| Boundary | Enforcement and negative evidence |
|---|---|
| Business home/wallet/pricing/list/detail | Business policy; trusted Business ID; detail ownership; other-Business tests |
| Create/fund/publish/start/review/top-up | Business policy + owning aggregate + legal gate where required + expected version + idempotency; no rates in Business contract |
| Creator discovery/join/participation/earnings | Active Creator identity; self-only subject; server-verified eligibility; owner participation check; no peer budgets |
| Customer offers/QR/status/history | Customer policy; Hybrid-only active funded participation; self-only session/history |
| Checkout resolve/confirm | Business/Cashier policy + current checkout permission and assigned Business; wrong-Business and used/expired token tests |
| Admin settings/payouts/settlements/oversight | PlatformAdmin policy and application guard; all other roles rejected |
| Deposit requests/review | Own Business submission/history; active Admin approval; exact credited journal and immutable reviewed history |
| Notifications/read/read-all | Current UserId AND role; server-owned recipient and route; no arbitrary target in input |
| Legal current/accept | Business/Creator self-only; exact current version/hash and explicit confirmation |
| Manual lookup | Disabled; authorized scanner shell only; no private identity enumeration |

Application services remain the financial boundary, not controllers. Business responses contain total costs only; Creator responses contain own earnings only; Customer responses omit budgets/Creator earnings/Platform revenue; Cashier receives safe offer identities and checkout result only. Public profiles have no phone/email/private-address columns. Public content links accept allowlisted HTTPS hosts; no server-side URL fetching or upload endpoint exists.

## Validation and abuse limits

API JSON: 32 KiB total, depth 16; both declared and unknown-length bodies bounded; unsupported media types/uploads rejected. Titles 120, descriptions 3000, requirements/content concept 2000, messages 1000, category/region 80, video reference 100, raw QR 43, ID token 8192 characters. IDs cannot be empty; UTC dates constrained to 2000–2100 and lifecycle ordering; amounts must fit numeric(18,2), positive where required; percentage scale <=4 and configured splits validated. JSON NaN/infinity/malformed numeric/ID values fail binding. No Business-type minimum wallet.

Fixed-window per trusted user (auth per peer IP), per minute: auth 20; QR issue 20; checkout 60; manual lookup 10; view refresh/content 12; join 10; deposits 10; payout/settlement 10; settings 12; notifications 90; reads 240; other writes 60. Global active-request limit 16, no queue. 429 includes Retry-After and a safe message. Proxy forwarding is trusted only for explicitly configured IPs. Development browser fixtures explicitly raise limits; Pilot/Production cannot use this override. These are per process; a later multi-instance deployment requires coordinated ingress limits.

## QR and financial integrity

32 cryptographically random bytes form the opaque URL-safe token; only SHA-256 digest persists. No token in a URL/query, ledger, outbox or diagnostic string. One five-minute session binds Customer/Business/Campaign/Creator/Creator Budget. Token lookup uses a fixed-length digest, not variable-time comparison of a raw secret; database lookup is not claimed constant-time. Wrong Business cannot consume it. Same valid idempotent retry returns its completed sale; competing redemptions commit at most once. Expiry observation changes status only and preserves history.

Existing serializable financial transactions, xmin/version checks, unique idempotency scopes, balanced immutable journals and reconciliation triggers remain authoritative. Deposit approval reuses the existing credit command in the same transaction as the Admin review. A deferred constraint verifies the approved deposit's exact journal/Business/amount. Creator-attributable costs continue consuming only that Creator Budget and its Campaign reserve. No automatic refunds, payouts, settlement or financial lifecycle posting is added to Worker.

`GET /api/admin/reconciliation` compares journal-derived wallet available/reserved, Campaign reserve, Creator Budget reserve, Creator payable, Customer cashback payable, Platform accrual and settlement against their projections. It is read-only and returns at most 100 mismatches; an empty list is not a repair action. Run reconciliation in an operationally quiet period or repeat a detected concurrent-write mismatch before escalation.

## Logs and headers

Structured logs contain correlation UUID, route template (not raw URL/query), status and bounded error classification. No request/response body logging, sensitive EF parameters, QR/ID tokens, proof content or private contacts. Provider delivery errors are replaced with owned error codes. Edge access logs must also omit queries, cookies, Authorization and bodies.

CSP: the API remains self-only for its JSON surface; the V3 Web edge allowlists only Firebase Auth endpoints (`identitytoolkit.googleapis.com`, `securetoken.googleapis.com`, `www.googleapis.com`) needed by custom-token/ID-token exchange. No wildcard, inline script or reCAPTCHA source is allowed. Permissions-Policy: `camera=(self), microphone=(), geolocation=(), payment=(), usb=()`. Referrer-Policy no-referrer; nosniff; X-Frame-Options DENY. HTTPS non-development responses have HSTS max-age=31536000. Do not add includeSubDomains/preload to shared domains without separate review. Separate static hosting must reproduce these tested headers; current acceptance serves the production Web output through the isolated API host. No current Production policy was changed.

Primary references: [Firebase ID token verification](https://firebase.google.com/docs/auth/admin/verify-id-tokens), [Firebase session guidance](https://firebase.google.com/docs/auth/admin/manage-cookies), [ASP.NET Core rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0), [ASP.NET Core CORS](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0).
