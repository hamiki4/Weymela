import { useEffect, useRef, useState } from "react";
import { Navigate, useNavigate } from "react-router-dom";
import { post, useAction, useResource } from "../api/client";
import type { Role, SessionProfile } from "../api/types";
import { Button, Field, Notice, Resource } from "../ui/components";
import { Brand } from "./Shell";
import { roleHome, useSession } from "./Session";
import {
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
  const [accountMode, setAccountMode] = useState<"signIn" | "signUp">("signIn");
  const [email, setEmail] = useState("");
  const [code, setCode] = useState("");
  const [codeSent, setCodeSent] = useState(false);
  const [busy, setBusy] = useState(false);
  const [profiles, setProfiles] = useState<SessionProfile[]>([]);
  const [selectedProfile, setSelectedProfile] = useState("");
  useEffect(() => {
    try {
      const config = readFirebasePublicConfig();
      const initialized = initializeFirebase(config);
      setAdapter(new FirebaseWebAuthAdapter(initialized.auth, exchangeFirebaseToken, initialized.persistenceReady));
    } catch {
      setError("Secure sign-in is unavailable right now.");
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
    await adapter.startEmailCode(email, accountMode === "signUp" ? "Signup" : "DeviceEnrollment");
    setCodeSent(true);
  });
  const confirmCode = () => submit(async () => {
    if (!adapter || !codeSent) throw new Error("Request an email code first.");
    await adapter.verifyEmailCode(email, accountMode === "signUp" ? "Signup" : "DeviceEnrollment", code);
    await onSignedIn();
  });
  return (
    <div className="sign-in-secure" aria-live="polite">
      <h1>Welcome to Weymela</h1>
      <p className="muted">We’ll send a verification code to your email.</p>
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
        <Field label={accountMode === "signIn" ? "Email or phone" : "Email address"}><input type={accountMode === "signIn" && !email.includes("@") ? "tel" : "email"} autoComplete={accountMode === "signIn" ? "username" : "email"} value={email} onChange={(e) => setEmail(e.target.value)} required disabled={codeSent} /></Field>
        {codeSent ? <Field label="Verification code" help="Enter the code we sent to your email."><input inputMode="numeric" autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value)} pattern="[0-9]{6}" required /></Field> : null}
        <Button type="submit" icon="arrow" disabled={busy}>{busy ? "Working…" : codeSent ? "Verify email" : "Send verification code"}</Button>
      </form> : null}
      {adapter ? <div className="auth-actions">
        <button className="text-action" type="button" onClick={() => { setAccountMode(accountMode === "signIn" ? "signUp" : "signIn"); setCodeSent(false); setError(null); }}>{accountMode === "signIn" ? "Create a new Weymela account" : "I already have an account"}</button>
      </div> : null}
      {adapter ? <p className="fine-print">Forgot your PIN? Recover it from the lock screen on your recognized device.</p> : null}
      {!adapter && !error ? <Notice>Preparing secure sign-in…</Notice> : null}
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
