# V3 Production Firebase and Resend configuration

This is configuration preparation only. It does not change the legacy Production stack or deploy V3.

## Production API settings

Configure these on the V3 Production API only. Keep the Resend key, Firebase service-account JSON, code-hash key, PIN pepper, and cookie certificate password out of the Web image, Worker, Git, and GitHub variables.

```text
V3__Auth__Provider=Firebase
V3__Auth__FirebaseProjectId=weymela-production
V3__Auth__EmailDeliveryMode=Resend
V3__Auth__FirebaseCustomTokenMode=FirebaseAdmin
V3__Auth__ResendApiKey=<injected from the existing protected secret reference Weymela-Production>
V3__Auth__ResendFromAddress=no-reply@mail.weymela.com
V3__Auth__ResendFromName=Weymela
V3__Auth__CodeHashKey=<independent random 32+ byte secret, strict base64>
V3__Auth__PinPepper=<different random 32+ byte secret, strict base64>
GOOGLE_APPLICATION_CREDENTIALS=/run/secrets/weymela-production-firebase-admin.json
V3__Auth__CookieKeyDirectory=/run/weymela-v3/production-keys
V3__Auth__CookieCertificatePath=/run/secrets/weymela-production-cookie-protection.pfx
V3__Auth__CookieCertificatePassword=<Production-only protected password>
```

The proposed sender is `no-reply@mail.weymela.com`; the domain is owner-confirmed as verified, but the sender has not been provider-validated or tested. Keep the existing `Weymela-Production` API key in the approved protected secret store and map its reference to `V3__Auth__ResendApiKey` for the V3 API only. The key label alone is not a deployable secret reference; do not put the key in this file, Compose, Git, or GitHub variables.

The existing Production service-account file is `/etc/creatorpay/firebase/weymela-production-fcm.json`. It identifies `weymela-production`, is `0640 root:65534`, and contains a parseable service-account private key. The V3 API image runs as `1654:1654`. Mount it read-only to the configured target and add only the API's supplementary group so that its existing group-read permission works; do not change the legacy file or make it world-readable. The API mount shape is:

```yaml
services:
  api:
    group_add:
      - "65534"
    environment:
      GOOGLE_APPLICATION_CREDENTIALS: /run/secrets/weymela-production-firebase-admin.json
    volumes:
      - /etc/creatorpay/firebase/weymela-production-fcm.json:/run/secrets/weymela-production-firebase-admin.json:ro
```

Use this on the V3 API service only. The private-key structure and project ID were checked locally, but live credential validity and `firebaseauth.users.delete` IAM permission remain unverified because the authorized Google API check could not reach Google. Confirm both before mounting it for V3. The API validates the credential file's project ID when its Firebase Admin signer initializes. Do not use either Pilot service-account file; prefer the narrow required permission over broad project roles.

The API also requires Production-specific protected cookie-key storage, a cookie certificate and password, the Production database connection, and the approved TLS/origin settings. Do not copy Pilot key material. Keep Firebase Admin credentials and the Resend API key out of Worker and Web configuration.

## Production Web build settings

The release workflow defaults to Pilot on branch pushes and on workflow dispatch. For a Production Web image, dispatch the immutable release workflow with `firebase_target=production`. Configure these non-secret GitHub repository variables from the existing Production Firebase Web app:

```text
VITE_FIREBASE_PRODUCTION_API_KEY
VITE_FIREBASE_PRODUCTION_AUTH_DOMAIN
VITE_FIREBASE_PRODUCTION_PROJECT_ID=weymela-production
VITE_FIREBASE_PRODUCTION_APP_ID
```

They are public Firebase Web configuration and are compiled into the Web image. The release manifest records both the selected target and exact Firebase project ID. The CI release workflow publishes immutable images and artifacts only; it does not deploy them.

The V3 client uses Firebase `signInWithCustomToken` after Weymela verifies email challenges. Firebase Email/Password and Phone sign-in providers are not required for this flow. Registration verification, device enrollment, PIN recovery, and password recovery use Weymela's bounded Resend email-code adapter; codes expire after 10 minutes, are limited to five verification attempts, and resend is throttled for 60 seconds. Recovery is not delegated to Firebase email templates. Provider sender acceptance and production delivery still require a protected key reference and provider validation; no email was sent during preparation.
