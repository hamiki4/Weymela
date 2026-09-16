# Phase 4A V2 source references

Phase 4A keeps the V3 identity, authentication, enrollment, legal and session
architectures authoritative. Weymela V2 was inspected as a read-only
presentation reference at `/opt/CreatorPayV2` (Git HEAD
`efaad620c4b47ae55038db58a99f9a909cfe9f50`). Its worktree contained
pre-existing uncommitted changes, so no V2 file was copied wholesale.

The following exact components informed the Phase 4A presentation:

- `src/CreatorPay.Web/src/AccountChrome.tsx`: restrained role accent carried
  through headings and primary actions. No account/session code was reused.
- `src/CreatorPay.Web/src/styles.css`: Customer green, Creator purple and
  Business blue role identity plus mobile spacing concepts. V3-owned tokens
  and accessibility rules implement the result.
- `src/CreatorPay.Web/src/AuthWorkspace.tsx`: Customer field inventory was
  reviewed. Only the need for a display/preferred name was retained; V2 email,
  phone, password and account-creation fields were deliberately excluded.
- `src/CreatorPay.Web/src/OnboardingStatus.tsx`: concise profile-state wording
  informed the existing `Already added`, `Pending` and unavailable states.
- `src/CreatorPay.Web/src/CustomerWorkspace.tsx`: mobile-first panel density
  was used as a layout reference. Customer discovery and QR behavior were not
  brought into Phase 4A.
- `src/CreatorPay.Web/src/RoleNavigation.tsx`: visible tap-target and
  non-hover-only interaction patterns were reviewed. V3 navigation remains
  authoritative.

No V2 authentication, credential, token, password-reset, account-creation,
financial, QR or Creator-to-Business partnership logic was reused.
