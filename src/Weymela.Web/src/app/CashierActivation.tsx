import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { post } from "../api/client";
import type { CashierActivationResult } from "../api/types";
import { Button, Field, Notice } from "../ui/components";
import { createFirebaseWebAuthAdapter } from "../auth/firebase";
import { Brand } from "./Shell";

export function CashierActivation() {
  const navigate = useNavigate();
  const [phone, setPhone] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  return (
    <main className="pin-setup-page">
      <section className="pin-setup-card" aria-labelledby="cashier-activation-title">
        <h1 id="cashier-activation-title">Activate Cashier access</h1>
        <p className="muted">
          Enter the phone number and temporary code provided by your Business.
        </p>
        {error ? <Notice error>{error}</Notice> : null}
        <form
          onSubmit={(event) => {
            event.preventDefault();
            setBusy(true);
            setError(null);
            void post<CashierActivationResult>("/auth/cashier/activate", {
              phone,
              activationCode: code,
            })
              .then(async (result) => {
                const adapter = createFirebaseWebAuthAdapter();
                if (!result.token?.customToken)
                  throw new Error("The activation session is invalid.");
                await adapter.signInWithCustomToken(result.token.customToken);
                navigate("/security-setup", { replace: true });
              })
              .catch((cause: unknown) => {
                setError(
                  cause instanceof Error
                    ? cause.message
                    : "We could not activate this Cashier account.",
                );
              })
              .finally(() => setBusy(false));
          }}
        >
          <fieldset disabled={busy}>
            <Field label="Phone number">
              <input
                type="tel"
                inputMode="tel"
                autoComplete="username"
                value={phone}
                onChange={(event) => setPhone(event.target.value)}
                required
              />
            </Field>
            <Field label="Activation code">
              <input
                inputMode="numeric"
                autoComplete="one-time-code"
                value={code}
                onChange={(event) => setCode(event.target.value)}
                minLength={6}
                maxLength={6}
                required
              />
            </Field>
            <Button type="submit">
              {busy ? "Activating…" : "Activate Cashier access"}
            </Button>
          </fieldset>
        </form>
      </section>
    </main>
  );
}
