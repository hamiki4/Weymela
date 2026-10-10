# V3 Production Firebase and Resend configuration

This is configuration preparation only. It does not change the legacy Production stack or deploy V3.

## Production API settings

Configure these on the V3 Production API only. Keep the Resend key, Firebase service-account JSON, code-hash key, PIN pepper, and cookie certificate password out of the Web image, Worker, Git, and GitHub variables.

```text
V3__Auth__Provider=Firebase
V3__Auth__FirebaseProjectId=weymela-production
V3__Auth__EmailDeliveryMode=Resend
V3__Auth__FirebaseCustomTokenMode=FirebaseAdmin
V3__Auth__ResendApiKey=<existing Weymela-Production key from the protected secret store>
V3__Auth__ResendFromAddress=<owner-selected address>@mail.weymela.com
V3__Auth__ResendFromName=Weymela
V3__Auth__CodeHashKey=<independent random 32+ byte secret, strict base64>
V3__Auth__PinPepper=<different random 32+ byte secret, strict base64>
GOOGLE_APPLICATION_CREDENTIALS=/run/secrets/weymela-production-firebase-admin.json
V3__Auth__CookieKeyDirectory=/run/weymela-v3/production-keys
V3__Auth__CookieCertificatePath=/run/secrets/weymela-production-cookie-protection.pfx
V3__Auth__CookieCertificatePassword=<Production-only protected password>
```

Mount the existing Production service-account file read-only into the API at the path above only after confirming the credential remains valid and has the `firebaseauth.users.delete` permission used by V3 account deletion. The existing host file is `/etc/creatorpay/firebase/weymela-production-fcm.json`; it identifies `weymela-production`, but its IAM permissions were not inspected. It is currently `0640 root:65534`; the V3 API image runs as `1654:1654`, so a direct mount is unreadable. Give only the V3 API a narrow read path, such as supplementary group `65534` on that read-only mount; do not make the file world-readable. Do not use either Pilot service-account file. The API validates the credential file's project ID when the Firebase Admin signer initializes. Prefer the narrow required permission over broad project roles.

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

The V3 client uses Firebase `signInWithCustomToken` after Weymela verifies email challenges. Firebase Email/Password and Phone sign-in providers are not required for this flow. Registration verification, device enrollment, PIN recovery, and password recovery use Weymela's bounded Resend email-code adapter; codes expire after 10 minutes, are limited to five verification attempts, and resend is throttled for 60 seconds. Recovery is not delegated to Firebase email templates.
