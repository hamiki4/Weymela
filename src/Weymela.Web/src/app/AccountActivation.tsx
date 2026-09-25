import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { post, useAction, useResource } from "../api/client";
import type { AccountLegalStatus } from "../api/types";
import { Button, Field, Notice, Resource } from "../ui/components";
import { useSession } from "./Session";

export function AccountActivation() {
  const { user } = useSession();
  return <main className="pin-setup-page"><section className="pin-setup-card">
    <h1>Activate your account</h1>
    {user ? <ActivationForm /> : <><p>Use the email address that received the invitation. Verify it and set your own password and device PIN before activating.</p><Link className="button primary" to="/sign-in">Create account or sign in</Link></>}
  </section></main>;
}

function ActivationForm() {
  const { signOut } = useSession();
  const navigate = useNavigate();
  const legal = useResource<AccountLegalStatus>("/onboarding/legal");
  const action = useAction();
  const [code, setCode] = useState("");
  const [accepted, setAccepted] = useState(false);
  const [complete, setComplete] = useState(false);

  const activate = () => void action.run(async key => {
    const token = code.trim();
    if (!/^[0-9a-fA-F]{32}-[A-Za-z0-9_-]{40,128}$/.test(token))
      throw new Error("Enter the complete activation code from your email.");
    const terms = legal.data?.documents.find(x => x.kind === "TermsOfService");
    const privacy = legal.data?.documents.find(x => x.kind === "PrivacyPolicy");
    const accountLegal = !legal.data?.current && accepted && terms && privacy ? {
      termsOfService: { documentId: terms.documentId, contentHash: terms.contentHash, accepted: true },
      privacyPolicy: { documentId: privacy.documentId, contentHash: privacy.contentHash, accepted: true },
    } : null;
    await post(`/onboarding/account-preauthorizations/${token.slice(0, 32)}/activate`, {
      activationSecret: token, accountLegal,
    }, key);
    setCode("");
    setComplete(true);
  });

  if (complete) return <><Notice>Your account is active. Sign in again to open your new profile.</Notice>
    <Button onClick={() => void signOut().then(() => navigate("/sign-in?intent=sign-in", { replace: true }))}>Continue to sign in</Button></>;
  return <><p>Enter the one-time code from your email after verifying your account and setting your password and device PIN.</p>
    <form onSubmit={event => { event.preventDefault(); activate(); }}>
      <Field label="Activation code"><input required autoComplete="off" value={code} onChange={event => setCode(event.target.value)} /></Field>
      <Resource resource={legal}>{status => !status.current && status.available ? <div className="legal-consent"><label><input type="checkbox" checked={accepted} onChange={event => setAccepted(event.target.checked)} /> I agree to the <a href="/legal/terms-of-service" target="_blank" rel="noreferrer">Terms of Service</a> and acknowledge the <a href="/legal/privacy-policy" target="_blank" rel="noreferrer">Privacy Policy</a> if activating a Customer profile.</label></div> : null}</Resource>
      {action.error && <Notice error>{action.error}</Notice>}
      <Button type="submit" disabled={action.busy || legal.loading || Boolean(legal.error)}>{action.busy ? "Activating…" : "Activate account"}</Button>
    </form></>;
}
