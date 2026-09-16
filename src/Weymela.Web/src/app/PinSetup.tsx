import { useState, type FormEvent } from "react";
import { Navigate, useNavigate } from "react-router-dom";
import { Button, Notice } from "../ui/components";
import { PinInput } from "../ui/PinInput";
import { roleHome, useSession } from "./Session";
import { Brand } from "./Shell";
import { AccountRedirect } from "./AccountEntry";

export function PinSetup() {
  const session = useSession();
  const navigate = useNavigate();
  const [pin, setPin] = useState("");
  const [confirmPin, setConfirmPin] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pinError, setPinError] = useState<string | null>(null);

  if (session.loading) return <main className="pin-setup-page">
    <section className="pin-setup-card" aria-live="polite">
      <Brand />
      <div role="status">Opening your PIN setup…</div>
    </section>
  </main>;
  if (session.loadFailed || session.user && (!session.accountSecurity || !session.deviceEnrollment
      || session.deviceEnrollment.state === "Unavailable"))
    return <main className="pin-setup-page">
      <section className="pin-setup-card" aria-labelledby="pin-setup-error-title">
        <Brand />
        <h1 id="pin-setup-error-title">We couldn't load your PIN setup.</h1>
        <Button type="button" onClick={() => void session.refresh()}>Try again</Button>
      </section>
    </main>;
  if (!session.user) return <Navigate to="/sign-in" replace />;
  if (!session.accountSecurity?.passwordEnrolled)
    return <AccountRedirect to="/security-setup" />;
  const state = session.deviceEnrollment?.state ?? "Unavailable";
  const canEnroll = state === "EnrollmentRequired";

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!/^[0-9]{5}$/.test(pin) || !/^[0-9]{5}$/.test(confirmPin)) {
      setError("Enter five digits for both PINs.");
      setPinError("Enter five digits for both PINs.");
      return;
    }
    if (pin !== confirmPin) {
      setError("PINs don't match. Try again.");
      setPinError("PINs don't match. Try again.");
      return;
    }
    setBusy(true);
    setError(null);
    setPinError(null);
    try {
      await session.enrollDevice(pin, confirmPin);
      setPin("");
      setConfirmPin("");
      navigate(roleHome[session.user!.role], { replace: true });
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Could not create your PIN. Try again.");
    } finally {
      setBusy(false);
    }
  };

  return <main className="pin-setup-page">
    <section className="pin-setup-card" aria-labelledby="pin-setup-title">
      <Brand />
      <h1 id="pin-setup-title">Create your PIN</h1>
      <p className="muted">Use this PIN to unlock Weymela on this device.</p>
      {error ? <Notice error>{error}</Notice> : null}
      {canEnroll ? <form noValidate onSubmit={(event) => void submit(event)}>
        <fieldset disabled={busy}>
          <PinInput label="Create PIN" value={pin} onChange={(value) => { setPin(value); setError(null); setPinError(null); }} error={pinError} autoFocus />
          <PinInput label="Confirm PIN" value={confirmPin} onChange={(value) => { setConfirmPin(value); setError(null); setPinError(null); }} error={pinError} />
          <Button type="submit" icon="arrow" disabled={busy}>{busy ? "Creating…" : "Continue"}</Button>
        </fieldset>
      </form> : <Notice error>
        {state === "RecoveryRequired"
          ? "Verify your email to create a new PIN."
          : state === "Expired" || state === "Revoked"
            ? "Sign in again to create your PIN."
            : "PIN setup is unavailable. Try again later."}
      </Notice>}
    </section>
  </main>;
}
