# V3 Pilot manual / physical-device acceptance

**ALL UNEXECUTED — MANUAL PILOT CHECK REQUIRED.** No checkbox below is a PASS. Execute only against separately authorized isolated V3, never existing V2 Pilot/Production. Record release commit/digests, database/host, tester/time/device/OS/browser, test account label, observed result and restricted evidence reference for each check. Test failures stop progression; no skipped release-critical item counts as PASS.

## Entry gates

- [ ] Capacity/headroom approved, isolated network/DB/roles and backup/restore rehearsal verified; three migrations match release manifest.
- [ ] No V2 listener/network/database/traffic changes; no Production cutover/DNS change implied.
- [ ] Real V3 HTTPS/headers/forwarding tested, trusted identities/configuration/legal publication ready; Development identity fails closed in Pilot.
- [ ] API/Web/Worker/DB healthy; Admin operations current, outbox healthy, reconciliation zero mismatches.
- [ ] Initially frozen: deposits/funding/checkout/payout mutations return safe unavailable state without changes. Any test financial activation has a separate explicit owner approval and isolated test-money policy.

## Admin

- [ ] Sign in as mapped Admin; other roles receive403 on Admin routes, no client-role escalation.
- [ ] Compact Financial Settings: both view modes, valid splits/thresholds/minimum Campaign budget, effective now/later; future version not early, old Campaign snapshot unchanged.
- [ ] Campaign list/detail: owning Business,budget/assigned/unassigned/used/remaining,Creator isolation,views/sales/earnings/cashback/revenue,audit links.
- [ ] Business and Creator oversight shows correct own funds/status/verified metrics; contacts permission-controlled.
- [ ] Payout tabs Creators / Customers / Platform / History; exact threshold payment/carry-forward, duplicate mark-paid prevented; no external provider implied.
- [ ] Platform partial settlement and remaining unsettled reconcile to existing revenue journal; retry cannot settle twice.
- [ ] Review real or explicitly authorized test deposit reference; approve/reject pending request,duplicate reference/retry protection,wrong role denied.
- [ ] Notification targets/read state and financial-setting affected-role targeting; delivery retries do not duplicate inbox entries.

## Business (A and B)

- [ ] Sign in; Advertising Funds shows Total,Available,Reserved; Total=Available+Reserved. No Business-type minimum wallet.
- [ ] Add Funds arbitrary positive amount (e.g.12.34),zero/negative rejected; pending request does not credit; Admin approval credits exactly once.
- [ ] Guided Create Campaign: View Only / View + Commission,budget,requirements,eligibility,dates; no editable financial split/rates.
- [ ] Pricing is clear; no Creator/Customer/Platform internal split. Funding confirmation shows before/reserved/after correctly.
- [ ] Fund reserves exact budget; insufficient Available fails atomically; a second Campaign cannot spend those reserved funds.
- [ ] Publish only after funded; eligible Creators can discover; no destructive End Campaign action.
- [ ] Applicants: public profile only,no phone/email/WhatsApp/private address. Approve & Set Budget/reject own only; Business B cannot manage A's Campaign.
- [ ] Creator Budget <=Available Campaign Budget; simultaneous assignments cannot over-allocate. BudgetA cannot fund CreatorB activity.
- [ ] Increase active Creator Budget; reduction unavailable/rejected server-side. Top-up uses unassigned Campaign reserve only.
- [ ] Completion releases unused Creator Budget to Campaign unassigned reserve,never Business Available; consumed funds stay earned.
- [ ] Wallet/history/current Campaigns remain usable with low Available; permitted deposits/account actions remain available.

## Creator

- [ ] Sign in as self; discovery only funded eligible Published/Active Campaigns,including negative category/region/follower cases.
- [ ] Join Campaign / Request to Join supports message/concept only; no overall type/budget/rate negotiation; duplicate request rejected.
- [ ] Missing/current/new legal version gates work; Business approval/rejection targets only correct Creator.
- [ ] Active Campaign shows Your Budget,Budget Remaining,verified/rewarded views,own earnings/content/status/dates; no Business wallet/other budgets/Customer details/Platform revenue.
- [ ] With approved manual evidence or live adapter: baseline once,verified delta,complete blocks,remainder,duplicate refresh,lower-count anomaly; no fabricated counts.
- [ ] Budget exhaustion pays no partial block,no negative balance,no rewarded-count advance without full funding; correct Business/Creator notification.
- [ ] How You Earn one table,Creator earnings only; Minimum to cash out wording,threshold-driven payout,no monthly/weekly date.
- [ ] View Reward + Sale Commission earnings accumulate across Campaigns; payout threshold/carry-forward and own history correct.

## Customer

- [ ] Only active View + Commission offers; View Only invisible and server rejects commerce attempts.
- [ ] Business/Creator/cashback,Watch Promotion,Get Directions,Get Offer QR; no internal financial details/private contacts.
- [ ] Offer page: working Back,Business name,Promoted by Creator,cashback,Get Offer QR; no redundant Promotion/Creator wording.
- [ ] Generated QR shows opaque token only,5-minute countdown,show-to-cashier text; replacement after expiry creates new token/history.
- [ ] Another Customer cannot access QR/history/cashback. Verified purchase cashback accumulates immediately; threshold carry-forward correct.

## Cashier / Business checkout

- [ ] Cashier assigned Business only; authorized Business Owner uses same Checkout service.
- [ ] Scan QR resolves safe Customer/Creator/Campaign/Business; scanner enters only Purchase Amount.
- [ ] Wrong Business fails without using QR or mutating balances; correct Business can subsequently redeem before expiry.
- [ ] Example purchase1000 at approved snapshot4.5/2/3.5 yieldsCreator45,Customer20,Platform35,Creator Budget−100; actual settings govern test values.
- [ ] Exact-budget boundary accepted; insufficient budget/expired/used QR rejected atomically; retry exact idempotency key returns same sale.
- [ ] Two authorized same-Business Cashier sessions concurrently redeem: at most one sale/journal; conflict safely rendered, no negative budget.
- [ ] Manual identity lookup remains disabled unless a separately approved secure resolver exists; never bypass QR into a second finance engine.

## Physical camera/device matrix

Repeat all camera checks on **iPhone Safari**, **installed iPhone PWA**, **Android Chrome**, **installed Android PWA**, **desktop webcam**. Record exact hardware/OS/browser versions.

- [ ] HTTPS and actual delivered `Permissions-Policy: camera=(self), microphone=(), geolocation=(), payment=(), usb=()`; no conflicting edge header.
- [ ] First permission grant,deny,deny→browser settings→allow; clear denied/unavailable/in-use states without a blank screen.
- [ ] Rear/front camera where available; portrait/landscape; scan full-frame center and edges. Visual guide must not crop decoder input.
- [ ] Navigate away/back,background/foreground,lock/unlock,close modal; no leaked stream/camera indicator after exit.
- [ ] Slow network/offline: no queued QR/financial writes,no automatic replay on reconnect. Expiry timer reconciles with server,not trusted device clock.
- [ ] PWA installation/icon/start URL; old release cache updates safely after finishing action; no cached private data after logout/account switch.

## Responsive / accessibility

- [ ]375,390,393,430px: intentional cards/2×2 grids,readable labels,stable touch controls,proper empty/loading/error/unauthorized states,no horizontal overflow/single-letter wrapping.
- [ ]768px tablet,1366×768,1440×900,1920×1080: constrained content,aligned tables/cards/inputs/actions,no giant empty space.
- [ ] Keyboard/tab/focus,labels/ARIA,contrast,disabled/loading; no hover-only actions,no purple hover/scaling/bouncing/glow.
- [ ] All Admin/Business/Creator major screens plus Customer QR/Cashier camera states checked; screenshots use artificial test identities only and stay outside source/images.

## Exit / operational safety

- [ ] Reconciliation zero mismatches before/after complete flow; inspect outbox backlog,notification failures,financial/QR conflicts.
- [ ] Bounded logs contain no raw QR/auth tokens,secrets or proof/private contact data; disk/swap/CPU remain within approved headroom under controlled load.
- [ ] Isolated V3 restart/rollback/restore rehearsal succeeds without touching V2; preserve all financial/audit/idempotency history.
- [ ] Record every unresolved issue/manual gap. Final owner acceptance is separate from deployment approval or financial unfreeze; no Production promotion implied.
