import { useState } from "react";
import { Navigate, useNavigate } from "react-router-dom";
import { post, useAction, useResource } from "../api/client";
import type { Role } from "../api/types";
import { Button, Field, Notice, Resource } from "../ui/components";
import { Brand } from "./Shell";
import { roleHome, useSession } from "./Session";

interface AuthMode {
  development: boolean;
  personas: { alias: string; name: string; role: Role }[] | null;
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
              <div>
                <h1>Welcome to Weymela</h1>
                <p>
                  Secure sign-in has not been connected in this environment yet.
                </p>
                <Notice>
                  Contact your workspace administrator. No development identity
                  is available here.
                </Notice>
              </div>
            )
          }
        </Resource>
      </div>
    </main>
  );
}
