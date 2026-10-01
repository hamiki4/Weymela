import { useEffect, useState } from "react";
import { Link, Navigate, useNavigate } from "react-router-dom";
import { post, useAction, useResource } from "../api/client";
import { actorRoleNameFromWire } from "../api/actorRoleContract";
import type { AccountLegalStatus, SessionProfile } from "../api/types";
import { Button, Empty, Field, Notice, PageHeader, Resource, Section } from "../ui/components";
import { RoleOnboardingShell, type OnboardingRole } from "./RoleOnboardingShell";
import { useSession } from "./Session";
import { CreatorSocialProfilesEditor } from "../features/creator/CreatorProfile";

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

export function Onboarding() {
  return <LegacyOnboarding />;
}

function statusLabel(status: Enrollment["status"]) {
  if (status === 0 || status === "Pending") return "Pending";
  if (status === 1 || status === "Approved") return "Approved";
  return "Rejected";
}

function LegacyOnboarding() {
  const navigate = useNavigate();
  const { user, loading, refresh, signOut } = useSession();
  const status = useResource<{ profiles: Enrollment[] }>("/onboarding/status");
  const legal = useResource<AccountLegalStatus>("/onboarding/legal");
  const action = useAction();
  const [role, setRole] = useState<OnboardingRole | null>(null);
  const [preferredName, setPreferredName] = useState("");
  const [legalAccepted, setLegalAccepted] = useState(false);
  const [displayName, setDisplayName] = useState("");
  const [region, setRegion] = useState("");
  const [socialUrls, setSocialUrls] = useState(emptySocial);
  const [submittedRole, setSubmittedRole] = useState<"Creator" | "Business" | null>(null);
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
  const submitCustomer = () => void action.run(async (key) => {
    const terms = legal.data?.documents.find((document) => document.kind === "TermsOfService");
    const privacy = legal.data?.documents.find((document) => document.kind === "PrivacyPolicy");
    if (!legal.data?.available || !terms || !privacy)
      throw new Error("Customer setup isn't available yet.");
    await post("/onboarding/profile", {
      role: "Customer",
      displayName: preferredName,
      accountLegal: {
        termsOfService: { documentId: terms.documentId, contentHash: terms.contentHash, accepted: legalAccepted },
        privacyPolicy: { documentId: privacy.documentId, contentHash: privacy.contentHash, accepted: legalAccepted },
      },
    }, key);
    cancel();
    status.reload();
    await refresh();
    navigate("/customer/offers", { replace: true });
  });
  const submitAdditional = (selected: "Creator" | "Business") => void action.run(async key => {
    const socialProfiles = socialPlatforms.filter(platform => socialUrls[platform].profileUrl.trim())
      .map(platform => ({ platform, profileUrl: socialUrls[platform].profileUrl.trim(), audienceCount: Number(socialUrls[platform].audience) || 0 }));
    if (selected === "Creator" && socialProfiles.length === 0) throw new Error("Add at least one social profile.");
    await post("/onboarding/profile", { role: selected, displayName: displayName.trim(),
      region: region.trim() || null, category: null, submission: null,
      socialProfiles: selected === "Creator" ? socialProfiles : [] }, key);
    cancel();
    setSubmittedRole(selected);
    status.reload();
  });

  return <main className="main-content page-shell section-kicker-space onboarding-page">
    <PageHeader eyebrow="Your Weymela account" title="How do you want to use Weymela?"
      action={<Button variant="quiet" onClick={() => void signOut()}>Sign out</Button>} />
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
              const disabled = state !== actionLabel;
              return <button key={choice} className={`profile-choice ${className}${role === choice ? " selected" : ""}`}
                type="button" aria-label={disabled ? `${choice} — ${state}` : `${actionLabel} — ${description}`}
                aria-pressed={role === choice} disabled={disabled} onClick={() => {
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
            <Resource resource={legal}>{(documents) => documents.available ? <form
              className="form-grid onboarding-form customer-onboarding-form" onSubmit={(event) => {
                event.preventDefault();
                submitCustomer();
              }}>
              <Field label="Preferred name" wide><input required autoComplete="name" maxLength={120}
                value={preferredName} onChange={(event) => setPreferredName(event.target.value)} /></Field>
              <div className="legal-consent form-wide"><div className="check-row">
                <input id="customer-legal-accepted" type="checkbox" checked={legalAccepted}
                  onChange={(event) => setLegalAccepted(event.target.checked)}
                  aria-label="I agree to the Terms of Service and acknowledge the Privacy Policy." required />
                <span id="customer-legal-consent-text"><label htmlFor="customer-legal-accepted">I agree to the </label>
                  <a href={documents.documents.find((document) => document.kind === "TermsOfService")!.viewPath}
                    target="_blank" rel="noreferrer">Terms of Service</a>
                  <label htmlFor="customer-legal-accepted"> and acknowledge the </label>
                  <a href={documents.documents.find((document) => document.kind === "PrivacyPolicy")!.viewPath}
                    target="_blank" rel="noreferrer">Privacy Policy</a>.</span>
              </div></div>
              {action.error && <Notice error>{action.error}</Notice>}
              <div className="form-footer"><Button type="button" variant="secondary" onClick={cancel}>Back</Button>
                <Button type="submit" disabled={action.busy}>{action.busy ? "Saving…" : "Continue"}</Button></div>
            </form> : <div className="onboarding-unavailable" role="status"><Empty icon="lock"
              title="Customer setup isn't available yet." message="Required terms and privacy information have not been published."
              action={<Button variant="secondary" onClick={cancel}>Back</Button>} /></div>}</Resource>
          </RoleOnboardingShell>}
          {(role === "Creator" || role === "Business") && !pending.has(role) && <RoleOnboardingShell role={role}
            title={role === "Creator" ? "Creator setup" : "Business setup"}
            description={role === "Creator" ? "Tell us about your Creator profile." : "Tell us about your Business."}>
            <div className="form-grid onboarding-form role-application-form">
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
              {action.error && <Notice error>{action.error}</Notice>}
              <div className="form-footer"><Button type="button" variant="secondary" onClick={cancel}>Back</Button>
                <Button type="button" onClick={() => submitAdditional(role)} disabled={action.busy || (role === "Creator" && !socialPlatforms.some(platform => socialUrls[platform].profileUrl.trim()))}>
                  {action.busy ? "Submitting…" : "Submit for Review"}</Button></div>
            </div>
          </RoleOnboardingShell>}
        </Section>
      </>;
    }}</Resource>
  </main>;
}
