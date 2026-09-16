import { useEffect, useRef, useState } from "react";
import { Navigate, useNavigate, useSearchParams } from "react-router-dom";
import { post, useAction, useResource } from "../api/client";
import type { Role, SessionProfile } from "../api/types";
import { Button, Field, Notice, Resource } from "../ui/components";
import { PasswordField } from "../ui/PasswordField";
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
type AuthView =
  | "landing"
  | "create"
  | "signIn"
  | "existingSetup"
  | "forgotPassword";

function FirebaseSignIn({
  onSignedIn,
  initialView,
}: {
  onSignedIn: () => Promise<void>;
  initialView: AuthView;
}) {
  const [view, setView] = useState<AuthView>(initialView);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [adapter, setAdapter] = useState<FirebaseWebAuthAdapter | null>(null);
  const redirectHandled = useRef(false);
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [code, setCode] = useState("");
  const [codeSent, setCodeSent] = useState(false);
  const [recoveryVerified, setRecoveryVerified] = useState(false);
  const [busy, setBusy] = useState(false);
  const [profiles, setProfiles] = useState<SessionProfile[]>([]);
  const [selectedProfile, setSelectedProfile] = useState("");

  useEffect(() => {
    try {
      const config = readFirebasePublicConfig();
      const initialized = initializeFirebase(config);
      setAdapter(
        new FirebaseWebAuthAdapter(
          initialized.auth,
          exchangeFirebaseToken,
          initialized.persistenceReady,
        ),
      );
    } catch {
      setError("Secure sign-in is unavailable right now.");
    }
  }, []);
  useEffect(() => {
    if (!adapter || redirectHandled.current) return;
    redirectHandled.current = true;
    void adapter
      .completeRedirect()
      .then((completed) => (completed ? onSignedIn() : undefined))
      .catch(() =>
        setError(
          "The sign-in attempt expired or was cancelled. Please try again.",
        ),
      );
  }, [adapter, onSignedIn]);

  const submit = async (operation: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await operation();
    } catch (cause) {
      if (cause instanceof ProfileSelectionRequiredError) {
        setProfiles(cause.profiles);
        setSelectedProfile(
          cause.profiles[0] ? profileValue(cause.profiles[0]) : "",
        );
        return;
      }
      setError(
        cause instanceof Error
          ? cause.message
          : "We could not complete sign-in. Please try again.",
      );
    } finally {
      setBusy(false);
    }
  };
  const changeView = (next: AuthView) => {
    setView(next);
    setError(null);
    setNotice(null);
    setCodeSent(false);
    setCode("");
    setRecoveryVerified(false);
    setPassword("");
    setConfirmPassword("");
    setProfiles([]);
  };
  const startAccountEmail = (purpose: "Signup" | "DeviceEnrollment") =>
    submit(async () => {
      if (!adapter) throw new Error("Secure sign-in is unavailable right now.");
      await adapter.startEmailCode(email, purpose);
      setCodeSent(true);
    });
  const verifyAccountEmail = (purpose: "Signup" | "DeviceEnrollment") =>
    submit(async () => {
      if (!adapter || !codeSent)
        throw new Error("Request an email code first.");
      await adapter.verifyEmailCode(email, purpose, code);
      await onSignedIn();
    });

  if (profiles.length > 1 && adapter)
    return (
      <div className="sign-in-secure" aria-live="polite">
        <h1>Choose a profile</h1>
        <p className="muted">Choose where you want to continue.</p>
        <div className="profile-choice" aria-label="Choose a profile">
          <select
            aria-label="Approved profile"
            value={selectedProfile}
            onChange={(event) => setSelectedProfile(event.target.value)}
          >
            {profiles.map((profile) => (
              <option key={profileValue(profile)} value={profileValue(profile)}>
                {profile.displayName} ·{" "}
                {profile.role === "PlatformAdmin"
                  ? "Platform Admin"
                  : profile.role}
              </option>
            ))}
          </select>
          <Button
            type="button"
            disabled={busy || !selectedProfile}
            onClick={() =>
              void submit(async () => {
                const profile = profiles.find(
                  (item) => profileValue(item) === selectedProfile,
                );
                if (!profile) throw new Error("Choose an approved profile.");
                await adapter.selectProfile(profile);
                setProfiles([]);
                await onSignedIn();
              })
            }
          >
            {busy ? "Opening…" : "Continue"}
          </Button>
        </div>
      </div>
    );

  return (
    <div className="sign-in-secure" aria-live="polite">
      {error ? <Notice error>{error}</Notice> : null}
      {notice ? <Notice>{notice}</Notice> : null}
      {!adapter && !error ? <Notice>Preparing secure sign-in…</Notice> : null}
      {view === "landing" ? (
        <section className="auth-landing" aria-labelledby="auth-landing-title">
          <h1 id="auth-landing-title">Welcome to Weymela</h1>
          <p className="muted">Create an account or sign in to continue.</p>
          <div className="auth-primary-actions">
            <Button
              type="button"
              icon="arrow"
              onClick={() => changeView("create")}
            >
              Create account
            </Button>
            <p>Already have an account?</p>
            <button
              className="secondary-auth-button"
              type="button"
              onClick={() => changeView("signIn")}
            >
              Sign in
            </button>
          </div>
        </section>
      ) : null}
      {view === "create" ? (
        <EmailAccountFlow
          title={codeSent ? "Check your email" : "Create your account"}
          email={email}
          setEmail={setEmail}
          code={code}
          setCode={setCode}
          codeSent={codeSent}
          verificationMessage="If this email can be used to create a Weymela account, you'll receive a verification code."
          busy={busy}
          onContinue={() => startAccountEmail("Signup")}
          onVerify={() => verifyAccountEmail("Signup")}
          onBack={() => changeView("landing")}
        />
      ) : null}
      {view === "existingSetup" ? (
        <EmailAccountFlow
          title={codeSent ? "Check your email" : "Set up sign-in"}
          email={email}
          setEmail={setEmail}
          code={code}
          setCode={setCode}
          codeSent={codeSent}
          verificationMessage="If this email is registered, you'll receive a verification code."
          busy={busy}
          onContinue={() => startAccountEmail("DeviceEnrollment")}
          onVerify={() => verifyAccountEmail("DeviceEnrollment")}
          onBack={() => changeView("signIn")}
        />
      ) : null}
      {view === "signIn" ? (
        <section aria-labelledby="full-sign-in-title">
          <h1 id="full-sign-in-title">Welcome back</h1>
          <form
            onSubmit={(event) => {
              event.preventDefault();
              void submit(async () => {
                if (!adapter)
                  throw new Error("Secure sign-in is unavailable right now.");
                await adapter.signInWithPassword(phone, password);
                setPassword("");
                await onSignedIn();
              });
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
                  autoFocus
                />
              </Field>
              <PasswordField
                label="Password"
                value={password}
                onChange={setPassword}
                autoComplete="current-password"
              />
              <Button type="submit" icon="arrow" disabled={busy}>
                {busy ? "Signing in…" : "Sign in"}
              </Button>
            </fieldset>
          </form>
          <div className="auth-link-stack">
            <button
              className="auth-secondary-action"
              type="button"
              onClick={() => changeView("forgotPassword")}
            >
              Forgot password?
            </button>
            <button
              className="auth-secondary-action"
              type="button"
              onClick={() => changeView("landing")}
            >
              Back
            </button>
          </div>
        </section>
      ) : null}
      {view === "forgotPassword" ? (
        <PasswordRecovery
          adapter={adapter}
          email={email}
          setEmail={setEmail}
          code={code}
          setCode={setCode}
          password={password}
          setPassword={setPassword}
          confirmPassword={confirmPassword}
          setConfirmPassword={setConfirmPassword}
          codeSent={codeSent}
          setCodeSent={setCodeSent}
          recoveryVerified={recoveryVerified}
          setRecoveryVerified={setRecoveryVerified}
          busy={busy}
          submit={submit}
          onDone={() => {
            changeView("signIn");
            setNotice(
              "Your password has been reset. Sign in with your new password.",
            );
          }}
          onBack={() => {
            void (adapter?.cancelPasswordRecovery() ?? Promise.resolve())
              .catch(() => undefined)
              .finally(() => changeView("signIn"));
          }}
        />
      ) : null}
    </div>
  );
}

function PasswordRecovery(props: {
  adapter: FirebaseWebAuthAdapter | null;
  email: string;
  setEmail: (v: string) => void;
  code: string;
  setCode: (v: string) => void;
  password: string;
  setPassword: (v: string) => void;
  confirmPassword: string;
  setConfirmPassword: (v: string) => void;
  codeSent: boolean;
  setCodeSent: (v: boolean) => void;
  recoveryVerified: boolean;
  setRecoveryVerified: (v: boolean) => void;
  busy: boolean;
  submit: (operation: () => Promise<void>) => Promise<void>;
  onDone: () => void;
  onBack: () => void;
}) {
  return (
    <section aria-labelledby="password-recovery-title">
      <h1 id="password-recovery-title">Reset your password</h1>
      {!props.codeSent ? (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            void props.submit(async () => {
              if (!props.adapter)
                throw new Error("Secure recovery is unavailable right now.");
              await props.adapter.startEmailCode(
                props.email,
                "PasswordRecovery",
              );
              props.setCodeSent(true);
            });
          }}
        >
          <fieldset disabled={props.busy}>
            <Field label="Email address">
              <input
                type="email"
                inputMode="email"
                autoComplete="email"
                value={props.email}
                onChange={(event) => props.setEmail(event.target.value)}
                required
                autoFocus
              />
            </Field>
            <Button type="submit" icon="arrow" disabled={props.busy}>
              {props.busy ? "Working…" : "Continue"}
            </Button>
          </fieldset>
        </form>
      ) : !props.recoveryVerified ? (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            void props.submit(async () => {
              if (!props.adapter)
                throw new Error("Secure recovery is unavailable right now.");
              await props.adapter.verifyPasswordRecovery(
                props.email,
                props.code,
              );
              props.setRecoveryVerified(true);
            });
          }}
        >
          <fieldset disabled={props.busy}>
            <h2>Check your email</h2>
            <p className="muted">
              If the email is registered, we sent a verification code.
            </p>
            <Field label="Verification code">
              <input
                inputMode="numeric"
                autoComplete="one-time-code"
                value={props.code}
                onChange={(event) => props.setCode(event.target.value)}
                pattern="[0-9]{6}"
                maxLength={6}
                required
                autoFocus
              />
            </Field>
            <Button type="submit" icon="arrow" disabled={props.busy}>
              {props.busy ? "Verifying…" : "Verify"}
            </Button>
          </fieldset>
        </form>
      ) : (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            void props.submit(async () => {
              if (!props.adapter)
                throw new Error("Secure recovery is unavailable right now.");
              if (props.password !== props.confirmPassword)
                throw new Error("Passwords don't match. Try again.");
              await props.adapter.resetPassword(
                props.password,
                props.confirmPassword,
              );
              props.onDone();
            });
          }}
        >
          <fieldset disabled={props.busy}>
            <PasswordField
              label="New password"
              value={props.password}
              onChange={props.setPassword}
              autoComplete="new-password"
              autoFocus
            />
            <PasswordField
              label="Confirm new password"
              value={props.confirmPassword}
              onChange={props.setConfirmPassword}
              autoComplete="new-password"
            />
            <p className="fine-print">
              Use at least 12 characters. You can use a passphrase.
            </p>
            <Button type="submit" icon="arrow" disabled={props.busy}>
              {props.busy ? "Resetting…" : "Reset password"}
            </Button>
          </fieldset>
        </form>
      )}
      <button
        className="auth-secondary-action"
        type="button"
        onClick={props.onBack}
      >
        Back to sign in
      </button>
    </section>
  );
}

function EmailAccountFlow(props: {
  title: string;
  email: string;
  setEmail: (v: string) => void;
  code: string;
  setCode: (v: string) => void;
  codeSent: boolean;
  verificationMessage: string;
  busy: boolean;
  onContinue: () => void;
  onVerify: () => void;
  onBack: () => void;
}) {
  return (
    <section aria-labelledby="email-account-title">
      <h1 id="email-account-title">{props.title}</h1>
      {props.codeSent ? (
        <p className="muted">{props.verificationMessage}</p>
      ) : null}
      <form
        onSubmit={(event) => {
          event.preventDefault();
          props.codeSent ? props.onVerify() : props.onContinue();
        }}
      >
        <fieldset disabled={props.busy}>
          {!props.codeSent ? (
            <Field label="Email address">
              <input
                type="email"
                inputMode="email"
                autoComplete="email"
                value={props.email}
                onChange={(event) => props.setEmail(event.target.value)}
                required
                autoFocus
              />
            </Field>
          ) : (
            <Field label="Verification code">
              <input
                inputMode="numeric"
                autoComplete="one-time-code"
                value={props.code}
                onChange={(event) => props.setCode(event.target.value)}
                pattern="[0-9]{6}"
                maxLength={6}
                required
                autoFocus
              />
            </Field>
          )}
          <Button type="submit" icon="arrow" disabled={props.busy}>
            {props.busy ? "Working…" : props.codeSent ? "Verify" : "Continue"}
          </Button>
        </fieldset>
      </form>
      <button
        className="auth-secondary-action"
        type="button"
        onClick={props.onBack}
      >
        Back
      </button>
    </section>
  );
}

function profileValue(profile: SessionProfile) {
  return `${profile.role}:${profile.subjectId}:${profile.businessId ?? "-"}`;
}

export function SignIn() {
  const mode = useResource<AuthMode>("/auth/mode");
  const [params] = useSearchParams();
  const [alias, setAlias] = useState("business");
  const [accessKey, setAccessKey] = useState("");
  const session = useSession();
  const action = useAction();
  const navigate = useNavigate();
  if (session.user)
    return <Navigate to={roleHome[session.user.role]} replace />;
  const initialView: AuthView =
    params.get("intent") === "sign-in" ? "signIn" : "landing";
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
        <p>Bring your business, creativity and community closer.</p>
        <img className="story-mark" src="/brand/weymela-mark.png" alt="" />
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
                <fieldset disabled={action.busy}>
                  <Field label="Workspace">
                    <select
                      value={alias}
                      onChange={(event) => setAlias(event.target.value)}
                    >
                      {data.personas?.map((x) => (
                        <option key={x.alias} value={x.alias}>
                          {x.name} ·{" "}
                          {x.role === "PlatformAdmin" ? "Admin" : x.role}
                        </option>
                      ))}
                    </select>
                  </Field>
                  <Field label="Local access code">
                    <input
                      type="password"
                      autoComplete="off"
                      value={accessKey}
                      onChange={(event) => setAccessKey(event.target.value)}
                      required
                    />
                  </Field>
                  {action.error ? <Notice error>{action.error}</Notice> : null}
                  <Button type="submit" icon="arrow" disabled={action.busy}>
                    {action.busy ? "Opening…" : "Open workspace"}
                  </Button>
                </fieldset>
              </form>
            ) : (
              <FirebaseSignIn
                onSignedIn={session.refresh}
                initialView={initialView}
              />
            )
          }
        </Resource>
      </div>
    </main>
  );
}
