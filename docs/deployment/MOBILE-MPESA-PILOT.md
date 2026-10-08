# Mobile and M-PESA Pilot release

Baseline: `b8064ace752e0ce9ca838ca46231f7f0c4d33d14`. Production is out of scope.

## Promotion visibility investigation (2026-10-08 UTC)

Read-only checks of the actual Pilot database and authenticated Business/Creator APIs found no promotion titled **Weekend Special**. The matching View + Sale record is **Weeekend**, `465d25c4-fb84-49ee-b9dd-971a6188dd31` (Business `abd`). It was published on September 30, is internally Active, has 5,000 ETB reserved and unallocated, and ends October 9 at 03:59:59 UTC. Its legacy application/content deadlines are null. Both TikTok and YouTube have an open slot.

The authenticated Creator `abd` has a blank saved region. The promotion requires `Addis ababa`. `CreatorEligibility.IsEligible` rejects that mismatch before the social-profile query. The deployed `/api/creator/discover` returns an empty list, and the browser correctly renders that response. This is not a frontend cache problem or missing funding. Legacy category/follower fields are not enforced by that predicate; platform-specific audience rules are separate. Do not misreport those legacy fields as the cause.

The separate Creator `Bell` has a matching saved region and active TikTok profile; global audience enforcement is off and this promotion's per-platform minimum is null. No live session for that identity was used in the baseline browser check. Do not claim authenticated acceptance for it merely from the stored eligibility data.

No promotion, Creator profile, deadline, restriction, or financial record was rewritten to force visibility. Business can review the intended region in Edit, and a missing Creator region requires the existing authorized profile-management process with the user's real region. The exact named promotion cannot be identified without its URL/ID if it is not the matching record above.

The release labels internally Active/Published Business promotions **Published**, not Customer-live. Creator participation Go Live still controls Customer visibility and the 30-day window. Discovery now excludes closed application windows for new applicants while preserving existing applications. Regression tests cover a funded, open View + Sale promotion titled Weekend Special, eligible/ineligible regions, unpublished exclusion, deadline enforcement, application and Business approval.

## M-PESA boundary

`Mpesa` is appended to the existing string-stored payout method enum; no ledger or schema replacement is required. Creator and Customer use their single verified registered phone, never an alternate form input. Bank destinations retain their existing encrypted storage and masking. Existing payout snapshots, amount bounds, authorization, atomic ledger posting, and idempotency remain unchanged.

M-PESA accepts Ethiopian Safaricom-format numbers (`+2517` plus eight digits, or `07` plus eight digits). This checks service compatibility, **not wallet ownership**. Safaricom describes M-PESA eligibility on its [Ethiopian service announcement](https://www.safaricom.et/en/whats-new/latest/news-and-blogs/safaricom-ethiopia-goes-live-with-m-pesa) and its [07 network announcement](https://www.safaricom.et/en/whats-new/latest/news-and-blogs/safaricom-ethiopia-launches-first-national-consumer-promotion-to-reward-over-1-million-customers).

Transfers remain manual and external. The authorized processor must verify the recipient/wallet through the actual provider, complete the transfer, and supply its external reference before recording payment. There is no configured automatic M-PESA transfer or wallet-lookup integration, and none is claimed. Phone verification alone is not evidence of wallet registration.

## Release gates

The initial merged release (`53c68e4`, run `37805610631`) passed every validation job but its Web image was blocked by HIGH `CVE-2026-4775` in Alpine `tiff 4.7.1-r0`. It was not deployed. The follow-up installs only the vendor-fixed, signed `tiff 4.7.2-r0` package, pinned to SHA-256 `757ce87ebe4923a6be9869d026e250958ca09696529c464781e8a91d2b81670b`, using the existing checksum/offline signature-verification pattern. Alpine's [security database](https://secdb.alpinelinux.org/v3.24/main.json) identifies this fixed version. The runtime version gate and regression test reject the vulnerable version; no scan exclusions or broad OS upgrades were introduced.

Use the protected PR workflow, all required validation jobs, and the immutable main release manifest. Take a fresh paired Pilot database/private-media backup, rehearse restore in an isolated database, verify migrations/grants, and deploy only digest-pinned API/Web/Worker images. Preserve the current Pilot financial-write configuration. Verify reconciliation, health and authenticated mobile/desktop English/Amharic UI. Keep session material outside the repository and revoke temporary UAT sessions afterwards.

Rollback uses the prior exact API/Web/Worker digests after compatibility checks. There is no database migration in this change. Never automatically restore a database over new financial activity; that requires separately approved recovery and reconciliation. Existing backups are server-local, not off-host disaster recovery.
