import { useState, type FormEvent } from "react";
import { ApiError } from "../api/client";
import { Button, Field, Notice } from "../ui/components";
import { useSession } from "./Session";

export function PinRecovery({ onCancel }: { onCancel: () => void }) {
  const session = useSession();
  const [step, setStep] = useState<"identifier" | "code">("identifier");
  const [identifier, setIdentifier] = useState("");
  const [code, setCode] = useState("");
  const [newPin, setNewPin] = useState("");
  const [confirmPin, setConfirmPin] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const start = async (event: FormEvent) => {
    event.preventDefault();
    if (!identifier.trim()) {
      setError("Enter a valid email address.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await session.startPinRecovery(identifier.trim());
      setStep("code");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "PIN recovery is temporarily unavailable.");
    } finally {
      setBusy(false);
    }
  };

  const complete = async (event: FormEvent) => {
    event.preventDefault();
    if (!/^[0-9]{6}$/.test(code)) {
      setError("Enter the six-digit email code.");
      return;
    }
    if (!/^[0-9]{5}$/.test(newPin)) {
      setError("Enter exactly five digits for your new PIN.");
      return;
    }
    if (newPin !== confirmPin) {
      setError("The PIN entries do not match.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await session.completePinRecovery(identifier.trim(), code, newPin, confirmPin);
      setCode("");
      setNewPin("");
      setConfirmPin("");
    } catch (cause) {
      if (cause instanceof ApiError && cause.code === "InvalidCode")
        setError("The code is invalid or expired.");
      else setError(cause instanceof Error ? cause.message : "PIN recovery could not be completed.");
    } finally {
      setBusy(false);
    }
  };

  return <div className="pin-recovery" aria-live="polite">
    <p className="eyebrow">Email recovery</p>
    <h1 id="recovery-title">Recover your Weymela PIN</h1>
    {step === "identifier"
      ? <>
          <p className="muted">We’ll send a code to your registered email.</p>
          {error ? <Notice error>{error}</Notice> : null}
          <form noValidate onSubmit={(event) => void start(event)}>
            <fieldset disabled={busy}>
              <Field label="Email address">
                <input type="email" inputMode="email" autoComplete="email" value={identifier}
                  onChange={(event) => setIdentifier(event.target.value)} required />
              </Field>
              <Button type="submit" icon="arrow" disabled={busy}>{busy ? "Sending…" : "Send verification code"}</Button>
            </fieldset>
          </form>
        </>
      : <>
          <p className="muted">If the account is eligible, a code was sent to its registered verified email.</p>
          {error ? <Notice error>{error}</Notice> : null}
          <form noValidate onSubmit={(event) => void complete(event)}>
            <fieldset disabled={busy}>
              <Field label="Email recovery code">
                <input type="text" inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}"
                  minLength={6} maxLength={6} value={code} onChange={(event) => setCode(event.target.value)} required />
              </Field>
              <Field label="New 5-digit PIN">
                <input type="password" inputMode="numeric" autoComplete="new-password" pattern="[0-9]{5}"
                  minLength={5} maxLength={5} value={newPin} onChange={(event) => setNewPin(event.target.value)} required />
              </Field>
              <Field label="Confirm new 5-digit PIN">
                <input type="password" inputMode="numeric" autoComplete="new-password" pattern="[0-9]{5}"
                  minLength={5} maxLength={5} value={confirmPin} onChange={(event) => setConfirmPin(event.target.value)} required />
              </Field>
              <Button type="submit" icon="lock" disabled={busy}>{busy ? "Recovering…" : "Recover device"}</Button>
            </fieldset>
          </form>
          <button className="text-button" type="button" disabled={busy}
            onClick={() => { setStep("identifier"); setCode(""); setError(null); }}>Send another code</button>
        </>}
    <button className="text-button" type="button" disabled={busy} onClick={onCancel}>Back to lock screen</button>
  </div>;
}
