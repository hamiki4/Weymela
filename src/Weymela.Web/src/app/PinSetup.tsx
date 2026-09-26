import { useRef, useState, type FormEvent } from "react";
import { Navigate } from "react-router-dom";
import { Button, Notice } from "../ui/components";
import { PinInput } from "../ui/PinInput";
import { useSession } from "./Session";
import { Brand } from "./Shell";
import { AccountRedirect } from "./AccountEntry";

export function PinSetup() {
  const session = useSession();
  const [pin, setPin] = useState("");
  const [confirmPin, setConfirmPin] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pinError, setPinError] = useState<string | null>(null);
  const submitting = useRef(false);

  if (!session.loading && (session.loadFailed || session.user && (!session.accountSecurity || !session.deviceEnrollment
      || session.deviceEnrollment.state === "Unavailable"))
  )
    return <main className="pin-setup-page">
      <section className="pin-setup-card" aria-labelledby="pin-setup-error-title">
        <Brand />
        <h1 id="pin-setup-error-title">We couldn't load your PIN setup.</h1>
        <Button type="button" onClick={() => void session.refresh()}>Try again</Button>
      </section>
    </main>;
  if (!session.user) return session.loading ? <main className="pin-setup-page"><section className="pin-setup-card"><Brand /><div role="status">Opening your PIN setup…</div></section></main> : <Navigate to="/sign-in" replace />;
  if (!session.accountSecurity?.passwordEnrolled)
    return <AccountRedirect to="/security-setup" />;
  const state = session.deviceEnrollment?.state ?? "Unavailable";
  const canEnroll = state === "EnrollmentRequired";

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (submitting.current) return;
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
    submitting.current = true;
    setBusy(true);
    setError(null);
    setPinError(null);
    try {
      await session.enrollDevice(pin, confirmPin);
      setPin("");
      setConfirmPin("");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Could not create your PIN. Try again.");
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  };

  return <main className="pin-setup-page">
    <section className="pin-setup-card" aria-labelledby="pin-setup-title">
      <Brand />
      <h1 id="pin-setup-title">Create your PIN</h1>
      <p className="muted">Use this PIN to unlock Weymela on this device.</p>
      {session.loading && <p role="status">Preparing your secure session…</p>}
      {error ? <Notice error>{error}</Notice> : null}
      {canEnroll ? <form noValidate onSubmit={(event) => void submit(event)}>
        <fieldset disabled={busy || session.loading}>
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
