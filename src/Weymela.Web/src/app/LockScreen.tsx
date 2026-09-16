import { useState, type FormEvent } from "react";
import { ApiError } from "../api/client";
import { Button, Notice } from "../ui/components";
import { PinInput } from "../ui/PinInput";
import { useSession } from "./Session";
import { Brand } from "./Shell";
import { PinRecovery } from "./PinRecovery";
import { useNavigate } from "react-router-dom";

export function LockScreen() {
  const session = useSession();
  const navigate = useNavigate();
  const state = session.deviceAccess?.state ?? "Locked";
  const [pin, setPin] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pinError, setPinError] = useState<string | null>(null);
  const [recovering, setRecovering] = useState(false);
  const canUnlock = state === "Locked";

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!/^[0-9]{5}$/.test(pin)) {
      setError("Enter your five-digit PIN.");
      setPinError("Enter your five-digit PIN.");
      return;
    }
    setBusy(true);
    setError(null);
    setPinError(null);
    try {
      await session.unlockDevice(pin);
      setPin("");
    } catch (cause) {
      if (cause instanceof ApiError && cause.code === "PinCooldown")
        setError("Too many PIN attempts. Wait before trying again.");
      else
        setError(
          cause instanceof Error
            ? cause.message
            : "Weymela could not be unlocked.",
        );
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="lock-page">
      <section
        className="lock-card"
        aria-labelledby={recovering ? "recovery-title" : "lock-title"}
      >
        <Brand />
        {recovering ? (
          <PinRecovery
            onCancel={() => {
              setRecovering(false);
              setError(null);
            }}
          />
        ) : (
          <>
            <h1 id="lock-title">Welcome back</h1>
            <p className="muted">Enter your PIN.</p>
            {state === "Cooldown" ? (
              <Notice error>
                Too many PIN attempts. Wait before trying again, or recover your
                PIN.
              </Notice>
            ) : null}
            {state === "RecoveryRequired" ? (
              <Notice error>Verify your email to reset your PIN.</Notice>
            ) : null}
            {state === "FullAuthenticationRequired" ? (
              <Notice error>Sign in again to continue.</Notice>
            ) : null}
            {error ? <Notice error>{error}</Notice> : null}
            {canUnlock ? (
              <form noValidate onSubmit={(event) => void submit(event)}>
                <fieldset disabled={busy}>
                  <PinInput
                    label="PIN"
                    value={pin}
                    onChange={(value) => {
                      setPin(value);
                      setError(null);
                      setPinError(null);
                    }}
                    error={pinError}
                    autoFocus
                  />
                  <Button type="submit" icon="lock" disabled={busy}>
                    {busy ? "Unlocking…" : "Unlock"}
                  </Button>
                </fieldset>
              </form>
            ) : null}
            <div className="lock-actions">
              {state !== "FullAuthenticationRequired" ? (
                <button
                  className="auth-secondary-action"
                  type="button"
                  onClick={() => {
                    setRecovering(true);
                    setError(null);
                  }}
                >
                  Forgot PIN?
                </button>
              ) : (
                <span />
              )}
              <button
                className="auth-secondary-action"
                type="button"
                onClick={() =>
                  void session
                    .signOut()
                    .then(() =>
                      navigate("/sign-in?intent=sign-in", { replace: true }),
                    )
                }
              >
                Sign in
              </button>
            </div>
          </>
        )}
      </section>
    </main>
  );
}
