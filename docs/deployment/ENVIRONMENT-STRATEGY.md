# Development, Pilot, and Production strategy

Development, Pilot, and Production each use separate PostgreSQL databases, credentials, secrets, networks, and versioned container tags. Configuration is injected at runtime; `.env*` files and secrets never enter Git. Firebase Auth is integrated through a fail-closed Web email-code/custom-token adapter and server token verifier; email delivery, signing configuration, Console enablement and trusted V3 identity provisioning remain deployment-owned gates. Phone is the preferred unverified login identifier and email is also accepted; SMS/Phone OTP is not used.

Images are built in GitHub Actions or an authorized external builder, scanned, signed, and pushed to a registry. Servers pull immutable digests; no Docker build occurs on a production/Pilot server. Promotion between environments requires an approved manifest, migration review, backup/restore evidence, health checks, and explicit freeze/unfreeze authorization.

Phase7's concrete [V3 Pilot preparation](V3-PILOT-PREPARATION.md) uses dedicated `weymela_v3_pilot` PostgreSQL,private V3 networks,loopback18080,new proposed hosts,and separate credentials. Existing V2 Pilot/Production are not environments to overwrite. Side-by-side deployment is presently capacity/adapter/legal/bootstrap gated; no GitHub push or runtime deployment has occurred.
