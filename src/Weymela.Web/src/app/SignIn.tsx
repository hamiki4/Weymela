import { useEffect, useRef, useState } from "react";
import { Navigate, useNavigate } from "react-router-dom";
import { post, useAction, useResource } from "../api/client";
import type { Role, SessionProfile } from "../api/types";
import { Button, Field, Notice, Resource } from "../ui/components";
import { Brand } from "./Shell";
import { roleHome, useSession } from "./Session";
import {
  FirebaseConfigurationError,
  ProfileSelectionRequiredError,
  FirebaseWebAuthAdapter,
  exchangeFirebaseToken,
  initializeFirebase,
  readFirebasePublicConfig,
} from "../auth/firebase";

interface AuthMode {
  development: boolean;
  personas: { alias: string; name: string; role: Role }[] | null;
}

function FirebaseSignIn({ onSignedIn }: { onSignedIn: () => Promise<void> }) {
  const [error, setError] = useState<string | null>(null);
  const [adapter, setAdapter] = useState<FirebaseWebAuthAdapter | null>(null);
  const redirectHandled = useRef(false);
  const [accountMode, setAccountMode] = useState<"signIn" | "signUp" | "recovery">("signIn");
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [code, setCode] = useState("");
  const [pin, setPin] = useState("");
  const [codeSent, setCodeSent] = useState(false);
  const [busy, setBusy] = useState(false);
  const [profiles, setProfiles] = useState<SessionProfile[]>([]);
  const [selectedProfile, setSelectedProfile] = useState("");
  useEffect(() => {
    try {
      const config = readFirebasePublicConfig();
      const initialized = initializeFirebase(config);
      setAdapter(new FirebaseWebAuthAdapter(initialized.auth, exchangeFirebaseToken, initialized.persistenceReady));
    } catch (cause) {
      setError(
        cause instanceof FirebaseConfigurationError
          ? cause.message
          : "Secure sign-in is unavailable in this environment.",
      );
    }
  }, []);
  useEffect(() => {
    if (!adapter) return;
    if (redirectHandled.current) return;
    redirectHandled.current = true;
    void adapter.completeRedirect().then((completed) => {
      if (completed) return onSignedIn();
    }).catch(() =>
      setError("The sign-in attempt expired or was cancelled. Please try again."),
    );
  }, [adapter, onSignedIn]);
  const submit = async (operation: () => Promise<void>) => {
    setBusy(true); setError(null);
    try { await operation(); } catch (cause) {
      if (cause instanceof ProfileSelectionRequiredError) {
        setProfiles(cause.profiles);
        setSelectedProfile(cause.profiles[0] ? `${cause.profiles[0].role}:${cause.profiles[0].subjectId}:${cause.profiles[0].businessId ?? "-"}` : "");
        setError(null);
        return;
      }
      setError(cause instanceof Error ? cause.message : "We could not complete sign-in. Please try again.");
    } finally { setBusy(false); }
  };
  const sendCode = () => submit(async () => {
    if (!adapter) throw new Error("Secure sign-in is unavailable in this environment.");
    await adapter.startEmailCode(email, accountMode === "recovery" ? "PinRecovery" : accountMode === "signUp" ? "Signup" : "DeviceEnrollment", accountMode === "signUp" ? phone : undefined);
    setCodeSent(true);
  });
  const confirmCode = () => submit(async () => {
    if (!adapter || !codeSent) throw new Error("Request an email code first.");
    if (accountMode === "recovery") {
      await adapter.resetPin(email, code, pin);
      setCodeSent(false); setCode(""); setPin(""); setAccountMode("signIn");
    } else {
      await adapter.verifyEmailCode(email, accountMode === "signUp" ? "Signup" : "DeviceEnrollment", code);
      await onSignedIn();
    }
  });
  return (
    <div className="sign-in-secure" aria-live="polite">
      <p className="eyebrow">Secure workspace sign-in</p>
      <h1>Welcome to Weymela</h1>
      {error ? <Notice error>{error}</Notice> : null}
      {profiles.length > 1 && adapter ? <div className="profile-choice" aria-label="Choose a profile">
        <p className="eyebrow">Choose a profile</p>
        <p className="muted">This account has more than one approved Weymela workspace.</p>
        <select aria-label="Approved profile" value={selectedProfile} onChange={(event) => setSelectedProfile(event.target.value)}>
          {profiles.map((profile) => <option key={`${profile.role}:${profile.subjectId}:${profile.businessId ?? "-"}`} value={`${profile.role}:${profile.subjectId}:${profile.businessId ?? "-"}`}>
            {profile.displayName} · {profile.role === "PlatformAdmin" ? "Platform Admin" : profile.role}
          </option>)}
        </select>
        <Button type="button" disabled={busy || !selectedProfile} onClick={() => submit(async () => {
          const profile = profiles.find((item) => `${item.role}:${item.subjectId}:${item.businessId ?? "-"}` === selectedProfile);
          if (!profile || !adapter) throw new Error("Choose an approved profile.");
          await adapter.selectProfile(profile); setProfiles([]); await onSignedIn();
        })}> {busy ? "Opening workspace…" : "Continue to profile"}</Button>
      </div> : null}
      {adapter ? <form onSubmit={(event) => { event.preventDefault(); void (codeSent ? confirmCode() : sendCode()); }}>
        {accountMode === "signUp" ? <Field label="Phone number" help="Your phone identifies the account; it is not verified by SMS."><input type="tel" autoComplete="tel" value={phone} onChange={(e) => setPhone(e.target.value)} required={!codeSent} disabled={codeSent} /></Field> : null}
        <Field label={accountMode === "signIn" ? "Phone number or registered email" : "Registered email address"} help="Verification and recovery codes are sent only to the registered email."><input type={accountMode === "signIn" && !email.includes("@") ? "tel" : "email"} autoComplete={accountMode === "signIn" ? "username" : "email"} value={email} onChange={(e) => setEmail(e.target.value)} required disabled={codeSent} /></Field>
        {codeSent ? <Field label="Email verification code"><input inputMode="numeric" autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value)} pattern="[0-9]{6}" required /></Field> : null}
        {accountMode === "recovery" && codeSent ? <Field label="New five-digit app PIN" help="A PIN unlocks an already enrolled device; it is not an Internet password."><input inputMode="numeric" autoComplete="new-password" value={pin} onChange={(e) => setPin(e.target.value)} pattern="[0-9]{5}" minLength={5} maxLength={5} required /></Field> : null}
        <Button type="submit" icon="arrow" disabled={busy}>{busy ? "Working…" : codeSent ? accountMode === "recovery" ? "Reset PIN" : "Verify email" : accountMode === "recovery" ? "Send recovery code" : accountMode === "signUp" ? "Send signup code" : "Send email code"}</Button>
      </form> : null}
      {adapter ? <div className="auth-actions">
        <button className="text-action" type="button" onClick={() => { setAccountMode(accountMode === "signIn" ? "signUp" : "signIn"); setCodeSent(false); setError(null); }}>{accountMode === "signIn" ? "Create a new Weymela account" : "I already have an account"}</button>
        <button className="text-action" type="button" onClick={() => { setAccountMode("recovery"); setCodeSent(false); setError(null); }}>Forgot PIN</button>
      </div> : null}
      {!adapter && !error ? <Notice>Preparing secure sign-in…</Notice> : null}
      <p className="fine-print">
        Sign-in uses a short-lived Firebase ID token only to establish a secure Weymela
        session. Tokens are not stored in the browser or sent in URLs.
      </p>
    </div>
  );
}

export function SignIn() {
  const mode = useResource<AuthMode>("/auth/mode");
  const [alias, setAlias] = useState("business");
  const [accessKey, setAccessKey] = useState("");
  const session = useSession();
  const action = useAction();
  const navigate = useNavigate();
  if (session.user)
    return <Navigate to={roleHome[session.user.role]} replace />;
  return (
    <main className="sign-in">
      <div className="sign-in-story">
        <Brand />
        <p className="eyebrow">Good stories. Real connections.</p>
        <h1>
          A place to
          <br />
          grow together.
        </h1>
        <p>Bring your Business, creativity and community closer.</p>
        <div className="story-mark" aria-hidden="true">
          w.
        </div>
      </div>
      <div className="sign-in-form">
        <Resource resource={mode}>
          {(data) =>
            data.development ? (
              <form
                onSubmit={(event) => {
                  event.preventDefault();
                  void action.run(async () => {
                    await post("/development/session", { alias, accessKey });
                    setAccessKey("");
                    await session.refresh();
                    const persona = data.personas?.find(
                      (x) => x.alias === alias,
                    );
                    if (persona) navigate(roleHome[persona.role]);
                  });
                }}
              >
                <p className="eyebrow">Development workspace</p>
                <h2>Welcome to Weymela</h2>
                <p className="muted">
                  Sample identities, isolated data. No real payment is taken.
                </p>
                <fieldset disabled={action.busy}>
                  <Field label="Workspace">
                    <select
                      value={alias}
                      onChange={(e) => setAlias(e.target.value)}
                    >
                      {data.personas?.map((x) => (
                        <option key={x.alias} value={x.alias}>
                          {x.name} ·{" "}
                          {x.role === "PlatformAdmin" ? "Admin" : x.role}
                        </option>
                      ))}
                    </select>
                  </Field>
                  <Field
                    label="Local access code"
                    help="Use the access code from your isolated development host."
                  >
                    <input
                      type="password"
                      autoComplete="off"
                      value={accessKey}
                      onChange={(e) => setAccessKey(e.target.value)}
                      required
                    />
                  </Field>
                  {action.error && <Notice error>{action.error}</Notice>}
                  <Button type="submit" icon="arrow" disabled={action.busy}>
                    {action.busy ? "Opening workspace…" : "Open workspace"}
                  </Button>
                </fieldset>
                <p className="fine-print">
                  Development sign-in is unavailable outside an explicitly
                  enabled local environment.
                </p>
              </form>
            ) : (
              <FirebaseSignIn onSignedIn={session.refresh} />
            )
          }
        </Resource>
      </div>
    </main>
  );
}
