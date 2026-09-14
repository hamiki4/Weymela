import { useState, type FormEvent } from "react";
import { Navigate, useNavigate } from "react-router-dom";
import { Button, Field, Notice } from "../ui/components";
import { roleHome, useSession } from "./Session";
import { Brand } from "./Shell";

export function PinSetup() {
  const session = useSession();
  const navigate = useNavigate();
  const [pin, setPin] = useState("");
  const [confirmPin, setConfirmPin] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!session.loading && !session.user) return <Navigate to="/sign-in" replace />;
  const state = session.deviceEnrollment?.state ?? "Unavailable";
  const canEnroll = state === "EnrollmentRequired";

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!/^[0-9]{5}$/.test(pin) || !/^[0-9]{5}$/.test(confirmPin)) {
      setError("Enter exactly five digits in both PIN fields.");
      return;
    }
    if (pin !== confirmPin) {
      setError("The PIN entries do not match.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await session.enrollDevice(pin, confirmPin);
      setPin("");
      setConfirmPin("");
      navigate(roleHome[session.user!.role], { replace: true });
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Secure device setup is temporarily unavailable.");
    } finally {
      setBusy(false);
    }
  };

  return <main className="pin-setup-page">
    <section className="pin-setup-card" aria-labelledby="pin-setup-title">
      <Brand />
      <p className="eyebrow">Authorized device</p>
      <h1 id="pin-setup-title">Set up your Weymela PIN</h1>
      <p className="muted">Use five digits you can remember. This PIN unlocks only this recognized device and is not your email verification code.</p>
      {error ? <Notice error>{error}</Notice> : null}
      {canEnroll ? <form noValidate onSubmit={(event) => void submit(event)}>
        <fieldset disabled={busy}>
          <Field label="5-digit PIN" help="Exactly five numbers.">
            <input type="password" inputMode="numeric" autoComplete="new-password" pattern="[0-9]{5}"
              minLength={5} maxLength={5} value={pin} onChange={(event) => setPin(event.target.value)} required />
          </Field>
          <Field label="Confirm 5-digit PIN">
            <input type="password" inputMode="numeric" autoComplete="new-password" pattern="[0-9]{5}"
              minLength={5} maxLength={5} value={confirmPin} onChange={(event) => setConfirmPin(event.target.value)} required />
          </Field>
          <Button type="submit" icon="arrow" disabled={busy}>{busy ? "Securing device…" : "Continue"}</Button>
        </fieldset>
      </form> : <Notice error>
        {state === "RecoveryRequired"
          ? "PIN setup is unavailable until verified-email recovery is completed."
          : state === "Expired" || state === "Revoked"
            ? "This device authorization is no longer active. Complete full account authentication before enrolling again."
            : "Secure device setup is temporarily unavailable. Please try again later."}
      </Notice>}
    </section>
  </main>;
}
