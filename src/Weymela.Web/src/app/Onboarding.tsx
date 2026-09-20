import { useState } from "react";
import { Navigate, useNavigate } from "react-router-dom";
import { post, useAction, useResource } from "../api/client";
import { actorRoleNameFromWire } from "../api/actorRoleContract";
import type { AccountLegalStatus, SessionProfile } from "../api/types";
import { Button, Empty, Field, Notice, PageHeader, Resource, Section } from "../ui/components";
import { RoleOnboardingShell, type OnboardingRole } from "./RoleOnboardingShell";
import { useSession } from "./Session";

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
  if (loading) return <div className="loading" role="status">Opening your account setup…</div>;
  if (!user) return <Navigate to="/sign-in" replace />;

  const approved = new Set((user.profiles ?? []).map((profile) => profile.role));
  const cancel = () => {
    setRole(null);
    setPreferredName("");
    setLegalAccepted(false);
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

  return <main className="main-content page-shell section-kicker-space onboarding-page">
    <PageHeader eyebrow="Your Weymela account" title="How do you want to use Weymela?"
      action={<Button variant="quiet" onClick={() => void signOut()}>Sign out</Button>} />
    <Resource resource={status}>{(data) => {
      const pending = new Set(data.profiles.filter((item) => item.status === "Pending" || item.status === 0)
        .map((item) => publicRole(item.role)));
      const unavailable = new Set(data.profiles.filter((item) => {
        const activeRole = publicRole(item.role);
        return (item.status === "Approved" || item.status === 1) && activeRole !== null
          && !approved.has(activeRole);
      }).map((item) => publicRole(item.role)));
      return <>
        {data.profiles.length > 0 && <Section title="Your profiles"><div className="stack-list">
          {data.profiles.map((item) => <div className="amount-row" key={item.id}><div><strong>
            {item.displayName || publicRole(item.role) || "Profile"} — {unavailable.has(publicRole(item.role))
              && (item.status === "Approved" || item.status === 1) ? "Unavailable" : statusLabel(item.status)}
          </strong>{item.decisionReason && <small>{item.decisionReason}</small>}</div></div>)}
        </div></Section>}
        <Section title="Choose a profile">
          <div className="content-grid profile-choice-grid">
            {choices.map(([choice, description, actionLabel, className]) => {
              const state = approved.has(choice) ? "Already added" : pending.has(choice) ? "Pending"
                : unavailable.has(choice) ? "Unavailable" : actionLabel;
              const disabled = state !== actionLabel;
              return <button key={choice} className={`profile-choice ${className}${role === choice ? " selected" : ""}`}
                type="button" aria-label={disabled ? `${choice} — ${state}` : `${actionLabel} — ${description}`}
                aria-pressed={role === choice} disabled={disabled} onClick={() => {
                  setRole(choice);
                  setPreferredName("");
                  setLegalAccepted(false);
                }}>
                <strong>{choice}</strong><span>{description}</span><span className="profile-choice-action">{state}</span>
              </button>;
            })}
            {approved.size === 3 && <p className="fine-print">All three profiles are active. Switch profile to use another one.</p>}
          </div>
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
          {(role === "Creator" || role === "Business") && <RoleOnboardingShell role={role}
            title={role === "Creator" ? "Creator setup" : "Business setup"}
            description={role === "Creator" ? "Your Creator profile is under review."
              : "Your Business profile is under review."}>
            <div className="onboarding-under-review" role="status"><p>Your profile request is under review. We’ll notify you when the review is complete.</p></div>
          </RoleOnboardingShell>}
        </Section>
      </>;
    }}</Resource>
  </main>;
}
