import { useRef, useState, type FormEvent } from "react";
import { Navigate } from "react-router-dom";
import { Button, Field, Notice } from "../ui/components";
import { PasswordField } from "../ui/PasswordField";
import { Brand } from "./Shell";
import { useSession } from "./Session";
import { AccountRedirect, accountEntryPath } from "./AccountEntry";

export function SecuritySetup() {
  const session = useSession();
  const [phone, setPhone] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const submitting = useRef(false);

  if (session.loading && !session.user) return <main className="pin-setup-page">
    <section className="pin-setup-card security-setup-card" aria-live="polite">
      <Brand />
      <div className="security-setup-status" role="status">Opening your account setup…</div>
    </section>
  </main>;
  if (!session.loading && (session.loadFailed || session.user && (!session.accountSecurity || !session.deviceEnrollment
      || session.deviceEnrollment.state === "Unavailable"))) return <main className="pin-setup-page">
    <section className="pin-setup-card security-setup-card" aria-labelledby="security-setup-error-title">
      <Brand />
      <h1 id="security-setup-error-title">We couldn't load your account setup.</h1>
      <Button type="button" onClick={() => void session.refresh()}>Try again</Button>
    </section>
  </main>;
  if (!session.user) return <Navigate to="/sign-in" replace />;
  if (!session.loading && session.accountSecurity?.passwordEnrolled)
    return <AccountRedirect to={accountEntryPath(session.user, session.accountSecurity, session.deviceEnrollment)} />;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (submitting.current) return;
    if (password.length < 12) { setError("Use at least 12 characters for your password."); return; }
    if (password !== confirmPassword) { setError("Passwords don't match. Try again."); return; }
    submitting.current = true;
    setBusy(true); setError(null);
    try {
      await session.enrollPassword(session.accountSecurity?.phoneEnrolled ? null : phone, password, confirmPassword);
      setPassword(""); setConfirmPassword("");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Account security setup could not be completed.");
    } finally { submitting.current = false; setBusy(false); }
  };

  return <main className="pin-setup-page">
    <section className="pin-setup-card security-setup-card" aria-labelledby="security-setup-title">
      <Brand />
      <h1 id="security-setup-title">Secure your account</h1>
      <p className="muted">Use your phone and password when you need to sign in again.</p>
      {session.loading && <p role="status">Preparing your secure session…</p>}
      {error ? <Notice error>{error}</Notice> : null}
      <form onSubmit={(event) => void submit(event)}>
        <fieldset disabled={busy || session.loading}>
          {!session.accountSecurity?.phoneEnrolled ? <Field label="Phone number">
            <input type="tel" inputMode="tel" autoComplete="tel" value={phone}
              onChange={(event) => setPhone(event.target.value)} required autoFocus />
          </Field> : <p className="security-ready">Your phone number is already registered.</p>}
          <PasswordField label="Password" value={password} onChange={setPassword}
            autoComplete="new-password" autoFocus={Boolean(session.accountSecurity?.phoneEnrolled)} />
          <PasswordField label="Confirm password" value={confirmPassword} onChange={setConfirmPassword}
            autoComplete="new-password" />
          <p className="fine-print">Use at least 12 characters. You can use a passphrase.</p>
          <Button type="submit" icon="arrow" disabled={busy}>{busy ? "Saving…" : "Continue"}</Button>
        </fieldset>
      </form>
    </section>
  </main>;
}
