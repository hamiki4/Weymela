import { useEffect, useRef, useState } from "react";
import { Link, Navigate, useNavigate, useSearchParams } from "react-router-dom";
import { post, useAction, useResource } from "../api/client";
import { actorRoleNameFromWire } from "../api/actorRoleContract";
import type { AccountLegalStatus, SessionProfile } from "../api/types";
import { Button, Empty, Field, Notice, PageHeader, Resource, Section } from "../ui/components";
import { RoleOnboardingShell, type OnboardingRole } from "./RoleOnboardingShell";
import { useSession } from "./Session";
import { CreatorSocialProfilesEditor } from "../features/creator/CreatorProfile";
import { clearSignupDraft, readSignupDraft, saveSignupDraft, type PublicSignupDraft } from "./signupDraft";
import { CompactLanguageChoice, useLanguage } from "../localization/Language";

type Enrollment = {
  id: string;
  role: OnboardingRole | number;
  status: "Pending" | "Approved" | "Rejected" | number;
  displayName: string;
  publicId?: string;
  submittedAtUtc: string;
  decisionReason: string | null;
};
const choices = [
  ["Customer", "Shop offers and use Weymela", "Use as Customer", "role-customer"],
  ["Creator", "Promote businesses and earn", "Become a Creator", "role-creator"],
  ["Business", "Create promotions with creators", "Add a Business", "role-business"],
] as const;
const socialPlatforms = ["TikTok", "YouTube", "Instagram", "Facebook"] as const;
type SocialPlatform = typeof socialPlatforms[number];
type OnboardingSocial = { profileUrl: string; audience: string };
const emptySocial = (): Record<SocialPlatform, OnboardingSocial> => ({ TikTok: { profileUrl: "", audience: "0" }, YouTube: { profileUrl: "", audience: "0" }, Instagram: { profileUrl: "", audience: "0" }, Facebook: { profileUrl: "", audience: "0" } });
function publicRole(role: Enrollment["role"]): OnboardingRole | null {
  const name = actorRoleNameFromWire(role);
  return name === "Customer" || name === "Creator" || name === "Business" ? name : null;
}

type RegistrationIdentity = { userId: string; email: string | null; phone: string | null; hasProfiles: boolean };

export function Onboarding() {
  const { user, loading } = useSession();
  const [params] = useSearchParams();
  const [draft, setDraft] = useState<PublicSignupDraft | null>(readSignupDraft);
  const [recover, setRecover] = useState(false);
  const identity = useResource<RegistrationIdentity>("/onboarding/identity");
  if (loading && !user) return <div role="status">Opening your account setup…</div>;
  if (!user) return <Navigate to="/sign-in" replace />;
  return <Resource resource={identity}>{account => {
    const matching = draft && draft.email.trim().toLowerCase() === account.email?.trim().toLowerCase()
      && (!draft.ownerUserId || draft.ownerUserId === account.userId);
    const completing = !recover && params.get("intent") !== "add" && !account.hasProfiles && draft;
    return completing ? <PublicSignupCompletion draft={draft} identity={account}
      identityMatches={!!matching} onRecover={() => setRecover(true)}
      onFinished={() => { setDraft(null); identity.reload(); }} />
      : <ProfileSetup key={account.userId} identity={account} initialDraft={recover && matching ? draft : null} onSaved={() => identity.reload()} />;
  }}</Resource>;
}

function PublicSignupCompletion({ draft, identity, identityMatches, onRecover, onFinished }: {
  draft: PublicSignupDraft; identity: RegistrationIdentity; identityMatches: boolean; onRecover: () => void; onFinished: () => void;
}) {
  const navigate = useNavigate();
  const { user, refresh, signOut } = useSession();
  const { language } = useLanguage();
  const legal = useResource<AccountLegalStatus>(language === "am" ? "/onboarding/legal?language=am" : "/onboarding/legal");
  const started = useRef(false);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [completed, setCompleted] = useState(false);
  const [accepted, setAccepted] = useState(false);
  const [reviewedDocuments, setReviewedDocuments] = useState<AccountLegalStatus["documents"] | null>(null);
  const needsConsent = identityMatches && legal.data?.required !== false && legal.data?.available && !legal.data.current;
  const documentFingerprint = legal.data?.documents.map(document => `${document.documentId}:${document.contentHash}`).join("|");
  useEffect(() => {
    setAccepted(false);
    setReviewedDocuments(null);
  }, [documentFingerprint]);
  const submit = async () => {
    if (!user) return;
    if (!identityMatches) throw new Error("These saved details belong to another email. Continue setup with the signed-in account or sign in to the original account.");
    saveSignupDraft({ ...draft, ownerUserId: identity.userId });
    if (!legal.data || (legal.data.required !== false && !legal.data.available))
      throw new Error("Your email is verified. Account activation requires published Terms of Service and Privacy Policy.");
    if (needsConsent && (!accepted || !reviewedDocuments))
      throw new Error("Review and accept the published policies to finish registration.");
    const documents = needsConsent ? reviewedDocuments : legal.data.documents;
    const terms = documents?.find(document => document.kind === "TermsOfService");
    const privacy = documents?.find(document => document.kind === "PrivacyPolicy");
    if (legal.data.required !== false && (!terms || !privacy))
      throw new Error("Your email is verified. Account activation requires published Terms of Service and Privacy Policy.");
    const accountLegal = legal.data.required === false || legal.data.current ? undefined : {
      termsOfService: { documentId: terms!.documentId, contentHash: terms!.contentHash, accepted: true },
      privacyPolicy: { documentId: privacy!.documentId, contentHash: privacy!.contentHash, accepted: true },
    };
    const socialProfiles = draft.role === "Creator" ? [{
      platform: draft.socialPlatform,
      profileUrl: draft.socialProfileUrl,
      audienceCount: Math.max(draft.followerCount ?? 0, draft.subscriberCount ?? 0),
      followerCount: draft.followerCount ?? 0,
      subscriberCount: draft.subscriberCount,
    }] : [];
    await post("/onboarding/registration", {
      role: draft.role,
      displayName: draft.role === "Business" ? draft.businessName : draft.legalName,
      legalName: draft.legalName,
      registeredPhone: identity.phone ? undefined : draft.phone,
      category: draft.role === "Business" ? draft.businessType : null,
      socialProfiles,
      ...(accountLegal ? { accountLegal } : {}),
    }, draft.idempotencyKey);
    clearSignupDraft();
    setCompleted(true);
  };
  useEffect(() => {
    if (!user || !legal.data || legal.loading || started.current || completed || error) return;
    if (needsConsent) { setBusy(false); return; }
    started.current = true;
    setBusy(true);
    void submit().catch(cause => {
      started.current = false;
      setError(cause instanceof Error ? cause.message : "We could not complete registration. Please try again.");
    }).finally(() => setBusy(false));
  }, [user, legal.data, legal.loading, needsConsent, completed, error]);
  if (!user) return <Navigate to="/sign-in" replace />;
  const continueToWorkspace = async () => {
    await refresh(true);
    onFinished();
    navigate(draft.role === "Business" ? "/business"
      : draft.role === "Customer" ? "/customer/offers" : "/onboarding", { replace: true });
  };
  return <main className="main-content public-signup-status">
    <div className={`signup-status-card signup-status-${draft.role.toLowerCase()}`}>
      <CompactLanguageChoice />
      <BrandStatus role={draft.role} />
      {busy && <><h1>Creating your account…</h1><p role="status">Your verified details are being saved securely.</p></>}
      {needsConsent && !busy && !error && !completed && <form onSubmit={event => {
        event.preventDefault();
        started.current = true;
        setBusy(true);
        void submit().catch(cause => {
          started.current = false;
          setError(cause instanceof Error ? cause.message : "Try again.");
        })
          .finally(() => setBusy(false));
      }}>
        <h1>Finish registration</h1>
        <p>Review and accept the published policies to finish registration.</p>
        <label className="signup-legal-consent"><input required type="checkbox" checked={accepted}
          onChange={event => {
            setAccepted(event.target.checked);
            setReviewedDocuments(event.target.checked ? legal.data!.documents : null);
          }} />
          <span>I agree to Weymela&apos;s <a href={language === "am" ? "/legal/terms-of-service?language=am" : "/legal/terms-of-service"} target="_blank" rel="noreferrer">Terms of Service</a> and acknowledge the <a href={language === "am" ? "/legal/privacy-policy?language=am" : "/legal/privacy-policy"} target="_blank" rel="noreferrer">Privacy Policy</a>.</span>
        </label>
        <div className="actions"><Button type="submit" disabled={!accepted}>Finish registration</Button>
          <Button type="button" variant="secondary" onClick={() => void signOut()}>Sign out</Button></div>
      </form>}
      {error && <><h1>Continue profile setup</h1><Notice error>{error}</Notice>
        <p>Your verified sign-in and saved profile details are retained. Retry when the requirement is resolved, or edit your profile details.</p>
        <p>For help, contact <a href="mailto:support@weymela.com">support@weymela.com</a>.</p>
        <div className="actions"><Button onClick={() => { setError(null); setAccepted(false); setReviewedDocuments(null); started.current = false; setBusy(true); legal.reload(); }}>Try again</Button>
          <Button variant="secondary" onClick={onRecover}>Edit profile details</Button>
          <Button variant="secondary" onClick={() => void signOut()}>Sign out</Button></div></>}
      {completed && <>
        <h1>{draft.role === "Business" ? "Your Business account is ready."
          : draft.role === "Customer" ? "Your account is ready." : "Your account is under review."}</h1>
        {draft.role === "Creator"
          ? <p>Your Creator account is waiting for Weymela approval. We will notify you when it is reviewed.</p>
          : <p>Your secure device is registered. You can continue to your workspace.</p>}
        <Button onClick={() => void continueToWorkspace()}>Continue</Button>
      </>}
    </div>
  </main>;
}

function BrandStatus({ role }: { role: PublicSignupDraft["role"] }) {
  return <p className="eyebrow">{role === "Business" ? "Business Owner" : role === "Creator" ? "Content Creator" : "Customer"}</p>;
}

function statusLabel(status: Enrollment["status"]) {
  if (status === 0 || status === "Pending") return "Pending";
  if (status === 1 || status === "Approved") return "Approved";
  return "Rejected";
}

type ProfileSetupDraft = {
  role: OnboardingRole | null; preferredName: string; displayName: string; region: string;
  socialUrls: Record<SocialPlatform, OnboardingSocial>; idempotencyKey: string;
  legalName?: string; category?: string;
};
function readProfileDraft(userId: string, initial: PublicSignupDraft | null): ProfileSetupDraft {
  try {
    const saved = JSON.parse(sessionStorage.getItem(`weymela.profile-setup.${userId}`) ?? "null") as ProfileSetupDraft | null;
    if (saved && [null, "Customer", "Creator", "Business"].includes(saved.role) && saved.idempotencyKey && saved.socialUrls) return saved;
  } catch { /* Keep verified identity when local progress is unavailable. */ }
  const socialUrls = emptySocial();
  if (initial?.role === "Creator" && socialPlatforms.includes(initial.socialPlatform as SocialPlatform))
    socialUrls[initial.socialPlatform as SocialPlatform] = { profileUrl: initial.socialProfileUrl ?? "", audience: String(initial.followerCount ?? 0) };
  return { role: initial?.role ?? null, preferredName: initial?.legalName ?? "",
    displayName: initial?.role === "Business" ? initial.businessName ?? "" : initial?.legalName ?? "",
    region: "", socialUrls, idempotencyKey: crypto.randomUUID(), legalName: initial?.legalName, category: initial?.businessType };
}

function ProfileSetup({ identity, initialDraft, onSaved }: {
  identity: RegistrationIdentity; initialDraft: PublicSignupDraft | null; onSaved: () => void;
}) {
  const navigate = useNavigate();
  const { user, loading, refresh, signOut, switchProfile } = useSession();
  const status = useResource<{ profiles: Enrollment[] }>("/onboarding/status");
  const { language } = useLanguage();
  const legal = useResource<AccountLegalStatus>(language === "am" ? "/onboarding/legal?language=am" : "/onboarding/legal");
  const action = useAction();
  const [saved] = useState(() => readProfileDraft(identity.userId, initialDraft));
  const [role, setRole] = useState<OnboardingRole | null>(saved.role);
  const [preferredName, setPreferredName] = useState(saved.preferredName);
  const [legalAccepted, setLegalAccepted] = useState(false);
  const [displayName, setDisplayName] = useState(saved.displayName);
  const [region, setRegion] = useState(saved.region);
  const [socialUrls, setSocialUrls] = useState(saved.socialUrls);
  const requestKey = useRef(saved.idempotencyKey);
  const [reviewedDocuments, setReviewedDocuments] = useState<AccountLegalStatus["documents"] | null>(null);
  const fingerprint = legal.data?.documents.map(document => `${document.documentId}:${document.contentHash}`).join("|");
  useEffect(() => { setLegalAccepted(false); setReviewedDocuments(null); }, [fingerprint]);
  useEffect(() => {
    sessionStorage.setItem(`weymela.profile-setup.${identity.userId}`, JSON.stringify({ role, preferredName,
      displayName, region, socialUrls, idempotencyKey: requestKey.current, legalName: saved.legalName, category: saved.category }));
  }, [identity.userId, role, preferredName, displayName, region, socialUrls, saved]);
  const endpoint = identity.hasProfiles ? "/onboarding/profiles" : "/onboarding/registration";
  const [submittedRole, setSubmittedRole] = useState<"Creator" | "Business" | null>(null);
  const production = import.meta.env.VITE_FIREBASE_PROJECT_ID === "weymela-production";
  useEffect(() => { status.reload(); void refresh(); }, []);
  if (loading) return <div className="loading" role="status">Opening your account setup…</div>;
  if (!user) return <Navigate to="/sign-in" replace />;

  const approved = new Set((user.profiles ?? []).map((profile) => profile.role));
  const cancel = () => {
    setRole(null);
    setPreferredName("");
    setLegalAccepted(false);
    setDisplayName(""); setRegion("");
    setSocialUrls(emptySocial());
  };
  const submitCustomer = () => void action.run(async () => {
    const documents = reviewedDocuments ?? legal.data?.documents;
    const terms = documents?.find((document) => document.kind === "TermsOfService");
    const privacy = documents?.find((document) => document.kind === "PrivacyPolicy");
    if (legal.data?.required !== false && (!legal.data?.available || !terms || !privacy || (!legal.data.current && !legalAccepted)))
      throw new Error(legal.data?.available ? "Accept Weymela's Rules and Regulations before continuing."
        : "Account activation requires published Terms of Service and Privacy Policy.");
    const accountLegal = legal.data?.required !== false && !legal.data?.current ? {
      termsOfService: { documentId: terms!.documentId, contentHash: terms!.contentHash, accepted: true },
      privacyPolicy: { documentId: privacy!.documentId, contentHash: privacy!.contentHash, accepted: true },
    } : undefined;
    await post(endpoint, {
      role: "Customer",
      displayName: preferredName,
      ...(saved.legalName ? { legalName: saved.legalName } : {}),
      ...(accountLegal ? { accountLegal } : {}),
    }, requestKey.current);
    const signup = readSignupDraft();
    if (signup?.ownerUserId === identity.userId || signup?.email.toLowerCase() === identity.email?.toLowerCase()) clearSignupDraft();
    onSaved();
    requestKey.current = crypto.randomUUID();
    cancel();
    status.reload();
    await refresh();
    navigate("/customer/offers", { replace: true });
  });
  const submitAdditional = (selected: "Creator" | "Business") => void action.run(async () => {
    const socialProfiles = socialPlatforms.filter(platform => socialUrls[platform].profileUrl.trim())
      .map(platform => ({ platform, profileUrl: socialUrls[platform].profileUrl.trim(), audienceCount: Number(socialUrls[platform].audience) || 0 }));
    if (selected === "Creator" && socialProfiles.length === 0) throw new Error("Add at least one social profile.");
    const documents = reviewedDocuments ?? legal.data?.documents;
    const terms = documents?.find((document) => document.kind === "TermsOfService");
    const privacy = documents?.find((document) => document.kind === "PrivacyPolicy");
    const requiresAccountLegal = legal.data?.required !== false && !legal.data?.current && (production || approved.size === 0);
    if (requiresAccountLegal && (!legal.data?.available || !terms || !privacy || !legalAccepted))
      throw new Error("Accept Weymela's Rules and Regulations before continuing.");
    const accountLegal = requiresAccountLegal ? {
      termsOfService: { documentId: terms!.documentId, contentHash: terms!.contentHash, accepted: true },
      privacyPolicy: { documentId: privacy!.documentId, contentHash: privacy!.contentHash, accepted: true },
    } : undefined;
    await post(endpoint, { role: selected, displayName: displayName.trim(),
      region: region.trim() || null, category: saved.category ?? null, submission: null,
      ...(saved.legalName ? { legalName: saved.legalName } : {}),
      socialProfiles: selected === "Creator" ? socialProfiles : [],
      ...(accountLegal ? { accountLegal } : {}) }, requestKey.current);
    const signup = readSignupDraft();
    if (signup?.ownerUserId === identity.userId || signup?.email.toLowerCase() === identity.email?.toLowerCase()) clearSignupDraft();
    onSaved();
    requestKey.current = crypto.randomUUID();
    cancel();
    if (selected === "Business") {
      await refresh(true);
      navigate("/business", { replace: true });
    } else {
      setSubmittedRole(selected);
      status.reload();
    }
  });

  return <main className="main-content page-shell section-kicker-space onboarding-page">
    <PageHeader eyebrow="Your Weymela account" title="How do you want to use Weymela?"
      action={<Button variant="quiet" onClick={() => void signOut()}>Sign out</Button>} />
    {identity.hasProfiles && <p><Link to="/">Return to your workspace</Link>. Your existing email, phone and credentials will be reused.</p>}
    <p>Received an invitation from Weymela? <Link to="/account/activate">Activate your account</Link>.</p>
    <Resource resource={status}>{(data) => {
      const pending = new Set(data.profiles.filter((item) => item.status === "Pending" || item.status === 0)
        .map((item) => publicRole(item.role)));
      if (submittedRole) pending.add(submittedRole);
      const unavailable = new Set(data.profiles.filter((item) => {
        const activeRole = publicRole(item.role);
        return (item.status === "Approved" || item.status === 1) && activeRole !== null
          && !approved.has(activeRole);
      }).map((item) => publicRole(item.role)));
      return <>
        {data.profiles.some(item => statusLabel(item.status) === "Rejected") && <Section title="Previous requests"><div className="stack-list">
          {data.profiles.filter(item => statusLabel(item.status) === "Rejected").map((item) => <div className="amount-row" key={item.id}><div><strong>
            {item.displayName || publicRole(item.role) || "Profile"} — {unavailable.has(publicRole(item.role))
              && (item.status === "Approved" || item.status === 1) ? "Unavailable" : statusLabel(item.status)}
          </strong>{item.decisionReason && <small>{item.decisionReason}</small>}</div></div>)}
        </div></Section>}
        <Section title="Choose a profile" className="onboarding-choice-section">
          <div className="content-grid profile-choice-grid profile-selection-options">
            {choices.map(([choice, description, actionLabel, className]) => {
              const state = approved.has(choice) ? "Already added" : pending.has(choice) ? "Under review"
                : unavailable.has(choice) ? "Unavailable" : actionLabel;
              const disabled = state !== actionLabel && state !== "Already added";
              return <button key={choice} className={`profile-choice ${className}${role === choice ? " selected" : ""}`}
                type="button" aria-label={state !== actionLabel ? `${choice} — ${state}` : `${actionLabel} — ${description}`}
                aria-pressed={role === choice} disabled={disabled} onClick={() => {
                  if (approved.has(choice)) {
                    const owned = user.profiles?.find(profile => profile.role === choice);
                    if (owned) void switchProfile(owned);
                    return;
                  }
                  requestKey.current = crypto.randomUUID();
                  setRole(choice);
                  setPreferredName("");
                  setLegalAccepted(false);
                  setDisplayName(""); setRegion("");
                  setSocialUrls(emptySocial()); setSubmittedRole(null);
                }}>
                <strong>{choice}</strong><span>{description}</span><span className="profile-choice-action">{state}</span>
              </button>;
            })}
            {approved.size === 3 && <p className="fine-print">All three profiles are active. Switch profile to use another one.</p>}
          </div>
          {(["Creator", "Business"] as const).filter(item => pending.has(item)).map(item =>
            <div className="onboarding-pending-line" role="status" key={item}><strong>Under review</strong>
              <span>Your {item} profile is waiting for approval.</span></div>)}
          {role === "Customer" && <RoleOnboardingShell role="Customer" title="Use as Customer"
            description="Choose the name you'd like to use in Weymela.">
            <Resource resource={legal}>{(documents) => documents.available || documents.required === false ? <form
              className="form-grid onboarding-form customer-onboarding-form" onSubmit={(event) => {
                event.preventDefault();
                submitCustomer();
              }}>
              <Field label="Preferred name" wide><input required autoComplete="name" maxLength={120}
                value={preferredName} onChange={(event) => setPreferredName(event.target.value)} /></Field>
              {documents.required !== false && !documents.current && <div className="legal-consent form-wide"><div className="check-row">
                <input id="customer-legal-accepted" type="checkbox" checked={legalAccepted}
                  onChange={(event) => { setLegalAccepted(event.target.checked); setReviewedDocuments(event.target.checked ? legal.data!.documents : null); }}
                  aria-label="I agree to Weymela's Rules and Regulations" required />
                <span id="customer-legal-consent-text"><label htmlFor="customer-legal-accepted">I agree to Weymela&apos;s <strong>Rules and Regulations</strong>.</label><br />
                  <small>By continuing, you also accept the current </small>
                  <a href={documents.documents.find((document) => document.kind === "TermsOfService")!.viewPath}
                    target="_blank" rel="noreferrer">Terms of Service</a><small> and acknowledge the </small>
                  <a href={documents.documents.find((document) => document.kind === "PrivacyPolicy")!.viewPath}
                    target="_blank" rel="noreferrer">Privacy Policy</a><small>.</small></span>
              </div></div>}
              {action.error && <Notice error>{action.error}</Notice>}
              <div className="form-footer"><Button type="button" variant="secondary" onClick={cancel}>Back</Button>
                <Button type="submit" disabled={action.busy}>{action.busy ? "Saving…" : "Continue"}</Button></div>
            </form> : <div className="onboarding-unavailable" role="status"><Empty icon="lock"
              title="Customer setup isn't available yet." message="Required terms and privacy information have not been published."
              action={<div className="actions"><Button variant="secondary" onClick={() => legal.reload()}>Try again</Button>
                <Button variant="secondary" onClick={cancel}>Back</Button>
                <a href="mailto:support@weymela.com">Contact support</a></div>} /></div>}</Resource>
          </RoleOnboardingShell>}
          {(role === "Creator" || role === "Business") && !pending.has(role) && <RoleOnboardingShell role={role}
            title={role === "Creator" ? "Creator setup" : "Business setup"}
            description={role === "Creator" ? "Tell us about your Creator profile." : "Tell us about your Business."}>
            <div className="form-grid onboarding-form role-application-form">
              {legal.data?.required !== false && !legal.loading && !legal.data?.available && <Notice error>
                <p>Account activation requires published Terms of Service and Privacy Policy.</p>
                <p>Your verified sign-in and saved profile details are retained. Retry when the requirement is resolved, or edit your profile details.</p>
                <Button variant="secondary" onClick={() => legal.reload()}>Try again</Button>{" "}
                <a href="mailto:support@weymela.com">Contact support</a>
              </Notice>}
              <Field label={role === "Creator" ? "Creator name" : "Business name"} wide>
                <input required maxLength={120} value={displayName} onChange={event => setDisplayName(event.target.value)} /></Field>
              <Field label="Region (optional)"><input maxLength={80} value={region} onChange={event => setRegion(event.target.value)} /></Field>
              {role === "Creator" && <div className="form-wide onboarding-social-profiles">
                <h4>Social Profiles</h4><p>Add at least one public profile link.</p>
                <CreatorSocialProfilesEditor
                  profiles={socialPlatforms.filter(platform => socialUrls[platform].profileUrl.trim()).map(platform => ({ platform, profileUrl: socialUrls[platform].profileUrl, selfReportedAudience: Number(socialUrls[platform].audience) || 0 }))}
                  onSave={(platform, profileUrl, audience) => setSocialUrls(current => ({ ...current, [platform]: { profileUrl, audience: String(audience) } }))}
                  onRemove={platform => setSocialUrls(current => ({ ...current, [platform]: { profileUrl: "", audience: "0" } }))}
                />
              </div>}
              {legal.data?.required !== false && (production || approved.size === 0) && !legal.data?.current && legal.data?.available && <div className="legal-consent form-wide"><div className="check-row">
                <input id="profile-legal-accepted" type="checkbox" checked={legalAccepted}
                  onChange={(event) => { setLegalAccepted(event.target.checked); setReviewedDocuments(event.target.checked ? legal.data!.documents : null); }}
                  aria-label="I agree to Weymela's Rules and Regulations" required />
                <span><label htmlFor="profile-legal-accepted">I agree to Weymela&apos;s <strong>Rules and Regulations</strong>.</label><br />
                  <small>By continuing, you also accept the current </small>
                  <a href={legal.data.documents.find((document) => document.kind === "TermsOfService")!.viewPath} target="_blank" rel="noreferrer">Terms of Service</a><small> and acknowledge the </small>
                  <a href={legal.data.documents.find((document) => document.kind === "PrivacyPolicy")!.viewPath} target="_blank" rel="noreferrer">Privacy Policy</a><small>.</small></span>
              </div></div>}
              {action.error && <Notice error>{action.error}</Notice>}
              <div className="form-footer"><Button type="button" variant="secondary" onClick={cancel}>Back</Button>
                <Button type="button" onClick={() => submitAdditional(role)} disabled={action.busy || (legal.data?.required !== false && (production || approved.size === 0) && !legal.data?.current && (!legal.data?.available || !legalAccepted)) || (role === "Creator" && !socialPlatforms.some(platform => socialUrls[platform].profileUrl.trim()))}>
                  {action.busy ? "Submitting…" : role === "Business" ? "Add Business" : "Submit for Review"}</Button></div>
            </div>
          </RoleOnboardingShell>}
        </Section>
      </>;
    }}</Resource>
  </main>;
}
