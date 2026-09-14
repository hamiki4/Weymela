import { useState, type FormEvent } from "react";
import { ApiError } from "../api/client";
import { Button, Field, Notice } from "../ui/components";
import { useSession } from "./Session";
import { Brand } from "./Shell";
import { PinRecovery } from "./PinRecovery";

export function LockScreen() {
  const session = useSession();
  const state = session.deviceAccess?.state ?? "Locked";
  const [pin, setPin] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [recovering, setRecovering] = useState(false);
  const canUnlock = state === "Locked";

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!/^[0-9]{5}$/.test(pin)) {
      setError("Enter your five-digit PIN.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await session.unlockDevice(pin);
      setPin("");
    } catch (cause) {
      if (cause instanceof ApiError && cause.code === "PinCooldown")
        setError("Too many PIN attempts. Wait before trying again.");
      else setError(cause instanceof Error ? cause.message : "Weymela could not be unlocked.");
    } finally {
      setBusy(false);
    }
  };

  return <main className="lock-page">
    <section className="lock-card" aria-labelledby={recovering ? "recovery-title" : "lock-title"}>
      <Brand />
      {recovering ? <PinRecovery onCancel={() => { setRecovering(false); setError(null); }} /> : <>
        <p className="eyebrow">Authorized device</p>
        <h1 id="lock-title">Weymela is locked</h1>
        <p className="muted">Your account is still signed in. Enter your device PIN to continue.</p>
        {state === "Cooldown" ? <Notice error>Too many PIN attempts. Wait before trying again, or recover your PIN.</Notice> : null}
        {state === "RecoveryRequired" ? <Notice error>Verified-email PIN recovery is required before this device can continue.</Notice> : null}
        {state === "FullAuthenticationRequired" ? <Notice error>This device session expired. Sign in fully to continue.</Notice> : null}
        {error ? <Notice error>{error}</Notice> : null}
        {canUnlock ? <form noValidate onSubmit={(event) => void submit(event)}>
          <fieldset disabled={busy}>
            <Field label="5-digit PIN">
              <input type="password" inputMode="numeric" autoComplete="current-password" pattern="[0-9]{5}"
                minLength={5} maxLength={5} value={pin} onChange={(event) => setPin(event.target.value)} required />
            </Field>
            <Button type="submit" icon="lock" disabled={busy}>{busy ? "Unlocking…" : "Unlock"}</Button>
          </fieldset>
        </form> : null}
        <div className="lock-actions">
          {state !== "FullAuthenticationRequired" ? <button className="text-button" type="button" onClick={() => { setRecovering(true); setError(null); }}>Forgot PIN</button> : <span />}
          <button className="text-button" type="button" onClick={() => void session.signOut()}>{state === "FullAuthenticationRequired" ? "Sign in fully" : "Sign out"}</button>
        </div>
      </>}
    </section>
  </main>;
}
