import { useState } from "react";
import { Navigate } from "react-router-dom";
import { post, useAction, useResource } from "../api/client";
import { useSession } from "./Session";
import { Button, Field, Notice, PageHeader, Resource, Section } from "../ui/components";
import type { AccountLegalStatus } from "../api/types";

type Enrollment = {
  id: string;
  role: "Customer" | "Creator" | "Business" | number;
  status: "Pending" | "Approved" | "Rejected" | number;
  displayName: string;
  publicId: string;
  submittedAtUtc: string;
  decisionReason: string | null;
};

function statusLabel(status: Enrollment["status"]) {
  if (status === 0 || status === "Pending") return "Under review";
  if (status === 1 || status === "Approved") return "Approved";
  return "Rejected";
}

export function Onboarding() {
  const { user, loading, refresh, signOut } = useSession();
  const status = useResource<{ profiles: Enrollment[] }>("/onboarding/status");
  const legal = useResource<AccountLegalStatus>("/onboarding/legal");
  const action = useAction();
  const [role, setRole] = useState<Enrollment["role"] | null>(null);
  const [form, setForm] = useState({ displayName: "", publicId: "", region: "", category: "", submission: "" });
  const [legalAccepted, setLegalAccepted] = useState(false);
  if (loading) return <div className="loading" role="status">Opening your account setup…</div>;
  if (!user) return <Navigate to="/sign-in" replace />;
  const approved = new Set((user.profiles ?? []).map((profile) => profile.role));
  const set = (name: keyof typeof form, value: string) => setForm((current) => ({ ...current, [name]: value }));
  const submit = () => void action.run(async (key) => {
    const terms = legal.data?.documents.find((document) => document.kind === "TermsOfService");
    const privacy = legal.data?.documents.find((document) => document.kind === "PrivacyPolicy");
    await post("/onboarding/profile", { role, ...form, accountLegal: role === "Customer" ? {
      termsOfService: terms && { documentId: terms.documentId, contentHash: terms.contentHash, accepted: legalAccepted },
      privacyPolicy: privacy && { documentId: privacy.documentId, contentHash: privacy.contentHash, accepted: legalAccepted }
    } : null }, key);
    setRole(null); setLegalAccepted(false); setForm({ displayName: "", publicId: "", region: "", category: "", submission: "" });
    status.reload(); await refresh();
  });
  return <main className="page-shell section-kicker-space onboarding-page">
    <PageHeader eyebrow="Your Weymela account" title="Choose how you want to use Weymela" description="Approved profiles share one account and keep their own workspace, permissions and financial records." action={<Button variant="quiet" onClick={() => void signOut()}>Sign out</Button>} />
    <Resource resource={status}>{(data) => <>
      {data.profiles.length > 0 && <Section title="Requests and profiles" description="Pending requests stay separate from active profile choices.">
        <div className="stack-list">{data.profiles.map((item) => <div className="amount-row" key={item.id}><div><strong>{item.displayName || item.role} — {statusLabel(item.status)}</strong><small>{item.publicId}{item.decisionReason ? ` · ${item.decisionReason}` : ""}</small></div></div>)}</div>
      </Section>}
      <Section title="Add a profile" description="You can add another approved profile without creating another account.">
        <div className="content-grid profile-choice-grid">
          {!approved.has("Customer") && <button className="profile-choice" type="button" onClick={() => setRole("Customer")}><strong>Use as Customer</strong><span>Discover eligible offers and keep your cashback history separate.</span></button>}
          {!approved.has("Creator") && <button className="profile-choice" type="button" onClick={() => setRole("Creator")}><strong>Become a Creator</strong><span>Submit your public creator details for Platform Admin review.</span></button>}
          {!approved.has("Business") && <button className="profile-choice" type="button" onClick={() => setRole("Business")}><strong>Add a Business</strong><span>Submit a new Business profile. Weymela creates the Business after approval.</span></button>}
          {approved.size === 3 && <p className="fine-print">All available profiles are active. Use “Switch profile” whenever you want to change workspace.</p>}
        </div>
        {role && <form className="form-grid onboarding-form" onSubmit={(event) => { event.preventDefault(); submit(); }}><h3>{role === "Creator" ? "Become a Creator" : role === "Business" ? "Add a Business" : "Use as Customer"}</h3>
          <Field label="Display name" wide><input required maxLength={120} value={form.displayName} onChange={(event) => set("displayName", event.target.value)} /></Field>
          <Field label="Public ID" help="A public identifier, not a phone number or email address."><input required maxLength={80} value={form.publicId} onChange={(event) => set("publicId", event.target.value)} /></Field>
          {role !== "Customer" && <><Field label="Region"><input maxLength={80} value={form.region} onChange={(event) => set("region", event.target.value)} /></Field><Field label="Category"><input maxLength={80} value={form.category} onChange={(event) => set("category", event.target.value)} /></Field><Field label="About this profile" wide><textarea maxLength={3000} rows={4} value={form.submission} onChange={(event) => set("submission", event.target.value)} /></Field></>}
          {role === "Customer" && <Resource resource={legal}>{(documents) => <div className="legal-consent form-wide">
            <p>Review the current account documents before activating your Customer profile.</p>
            <ul>{documents.documents.map((document) => <li key={document.kind}><a href={document.viewPath} target="_blank" rel="noreferrer">{document.title}</a> <small>Version {document.version}</small></li>)}</ul>
            <label className="check-row"><input type="checkbox" checked={legalAccepted} onChange={(event) => setLegalAccepted(event.target.checked)} required /> <span>I have read and accept the Terms of Service and Privacy Policy.</span></label>
          </div>}</Resource>}
          {action.error && <Notice error>{action.error}</Notice>}<div className="form-footer"><Button type="button" variant="secondary" onClick={() => setRole(null)}>Cancel</Button><Button type="submit" disabled={action.busy}>{action.busy ? "Saving…" : role === "Customer" ? "Continue" : "Submit for review"}</Button></div>
        </form>}
      </Section>
    </>}</Resource>
  </main>;
}
