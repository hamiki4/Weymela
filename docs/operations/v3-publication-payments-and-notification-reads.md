# V3 publication payments and notification reads

Content approval previously consumed the UGC reserve and posted Creator earnings and platform revenue. Publication Go Live only updated metadata. This allowed a genuine earned-balance credit before publication. Fixed UGC fees now remain reserved and pending until approved content has verified publication and Go Live commits. No minimum view count applies to this fee. View rewards retain verified-view requirements.

Go Live posts one balanced journal, one active earning, platform revenue, and one earned-payment event in a serializable transaction. The database independently requires approved, verified, evidenced publication for new UGC earnings. Concurrent changes may return the existing explicit concurrency response; retrying cannot pay twice. Receipt uploads, applications, content submission, and Business approval cannot earn or deposit funds.

## Historical correction

Migration 32 adds an immutable correction table and its integrity constraints; it does not automatically reverse any financial history. The existing Bootstrap operator supports `--operation ugc-payment-correction`, with explicit target, Platform Admin user, assignment, original journal, authorization reference, and idempotency key. Production requires the actual owner authorization reference. There is no public correction endpoint or startup correction.

The command accepts only an approved assignment that never went live, whose original approval journal exactly matches its frozen fee, whose premature earning remains available, and for which no later external payout was confirmed. It preserves the original journal and earning. A new balanced adjustment debits Creator payable and platform revenue and restores the full Business reserve. Reports show the original earning as reversed and the explicit correction separately. Payout eligibility excludes unverified historical UGC credits even before a correction is applied.

Take and restore-test a fresh backup before migration. Rehearse migration, exact API/Worker/backup grants, and the specific correction in the restored, network-isolated database. Apply the verified migration bundle and grants before starting the new API and Worker. Once a correction exists, recover by rolling forward; migration Down refuses to remove correction history. The separate V1 routing rollback, Pilot, backups, Firebase identities and Android artifacts are unaffected.

## Publication verification

TikTok canonical, mobile and supported short/share links are normalized. Short links follow a bounded number of HTTPS redirects on an exact TikTok host allowlist. The resolver pins connections to public DNS results, rejects private/link-local/tunnel addresses, disables implicit redirects/proxies/cookies, strips share parameters, and does not parse arbitrary pages. A resolved URL establishes a post ID only.

Fixed-fee publication verification requires ownership and content-identity evidence independently of view-count capability. Production with no configured social verification adapter reports provider unavailable and retains the pending verification. An authorized Platform Admin may use the existing publication review screen to record genuine public-post and ownership evidence. A URL alone must never be accepted as verification. Invalid links, inaccessible posts, wrong Creator and provider failures have distinct results.

## Notification behavior

Opening an inbox item acknowledges it through the scoped server API before navigation. Visible, loaded resource records acknowledge only matching notification resources; prefetch, loading placeholders and hidden tabs do not. Parent campaign displays do not acknowledge unrelated leaf items. Screen-level notifications without a resource identity may be acknowledged only by their exact displayed route. Reads are idempotent, persisted and scoped to authenticated user and active role. Badge counts refresh immediately after local reads and reconcile on focus and polling across sessions.

## Owner smoke tests after deployment

1. On the Production website, register separate Customer, Creator and Business test identities. Verify email yourself, finish profile and PIN, and check dashboard or the correct approval state. The agent needs no inbox access. Check policy links and the actual consent state; unavailable documents must not produce fictitious acceptance.
2. With existing verified accounts, test Business → Customer, Customer → Creator and Creator → Business. Retry and refresh, add an already-owned profile, then switch profiles. Own phone/email reuse must work; a different identity's phone must be rejected. Check Business-scoped Cashier enrollment and restricted Admin enrollment.
3. Create two unrelated notifications. Open one through the inbox and another through its actual related item. Check badge, refresh, sign-out/in, another session and another profile. Unrelated notifications must remain unread.
4. Create and fund a synthetic UGC campaign. Open Edit details and confirm the existing editor works; approved financial terms must stay locked. Accept, submit, request revisions and approve content. Check Pending/Active/Completed tracking and pending earnings. Approval must not increase available earnings. Submit a canonical or share URL from the selected Creator's own public account. Confirm automatic-provider availability honestly, or perform authorized Admin review with real evidence. Verified Go Live must earn the agreed fixed fee exactly once, including repeated submissions.
5. Inspect the corrected historical assignment: original journal preserved, explicit correction visible, 4,900 ETB pending, 5,000 ETB reserved, no external payout. Publish only actual approved content and verify it genuinely before Go Live.
6. Check Creator payout minimum, pending/available/paid-out/history, and destination setup. Mobile money requires independently verified eligible phone ownership. Payouts remain Admin-controlled; do not record external payment without actual payment evidence.
7. Check all three Business deposit destinations, private receipt upload, pending review, rejection, and settlement-backed approval. Upload alone must not credit funds. Check protected Admin financial settings and original historical terms.
8. Check Customer discovery/QR/cashback, Business and Cashier checkout, duplicate QR rejection, notifications, language and mobile navigation. Verify the installed Android versionCode 11 against the updated API separately. Its embedded Web assets are unchanged by a website deployment; no new AAB is part of this release.

Automated isolated provider/settlement simulations do not establish live TikTok ownership, real settlement, external payout or authenticated owner registration results.
