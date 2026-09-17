import { useEffect, useRef, useState } from "react";
import { Navigate, useNavigate, useSearchParams } from "react-router-dom";
import { post, request, useResource } from "../api/client";
import type { Role } from "../api/types";
import { Button, Notice } from "../ui/components";
import { useSession } from "./Session";

type PublicRole = "Customer" | "Creator" | "Business" | "PlatformAdmin";
type Purpose = "PROFILE_ONBOARDING" | "EXISTING_WORKSPACE";
export type ProductIntegrationConfiguration = { enabled: boolean; beginUrl: string; callbackId: string };
type Handoff = { code: string; state: string; callbackUrl: string; expiresAtUtc: string };

function submitForm(action: string, fields: Record<string, string>) {
  const form = document.createElement("form");
  form.method = "post";
  form.action = action;
  form.style.display = "none";
  for (const [name, value] of Object.entries(fields)) {
    const input = document.createElement("input");
    input.type = "hidden";
    input.name = name;
    input.value = value;
    form.append(input);
  }
  document.body.append(form);
  form.submit();
}

export async function beginProductHandoff(role: PublicRole, purpose: Purpose) {
  const configuration = await request<ProductIntegrationConfiguration>("/integration/product/configuration");
  if (!configuration.enabled) throw new Error("The product workspace integration is not enabled.");
  submitForm(configuration.beginUrl, { role, purpose });
}

export function useProductIntegrationConfiguration() {
  return useResource<ProductIntegrationConfiguration>("/integration/product/configuration");
}

export function ProductWorkspaceEntry({ role, fallback }: { role: PublicRole; fallback: React.ReactNode }) {
  const session = useSession();
  const configuration = useProductIntegrationConfiguration();
  const started = useRef(false);
  const [error, setError] = useState("");
  const begin = () => {
    setError("");
    started.current = true;
    void beginProductHandoff(role, "EXISTING_WORKSPACE").catch((reason) => {
      started.current = false;
      setError(reason instanceof Error ? reason.message : "We couldn't open your workspace.");
    });
  };
  useEffect(() => {
    if (!configuration.loading && configuration.data?.enabled && !session.loading
        && session.user?.role === role && !started.current) begin();
  }, [configuration.data, configuration.loading, role, session.loading, session.user]);
  if (!session.loading && (!session.user || session.user.role !== role))
    return <Navigate to="/onboarding" replace />;
  if (!configuration.loading && configuration.data && !configuration.data.enabled) return <>{fallback}</>;
  if (configuration.error)
    return <main className="loading product-handoff-state" role="status"><p>We couldn't verify the product workspace integration.</p></main>;
  return <main className="loading product-handoff-state" role="status"><p>{error || "Opening your Weymela workspace…"}</p>{error && <Button onClick={begin}>Try again</Button>}</main>;
}

export function ProductHandoffCallback() {
  const [parameters] = useSearchParams();
  const [error, setError] = useState("");
  const started = useRef(false);
  const state = parameters.get("state") ?? "";
  const role = parameters.get("role") ?? "";
  const purpose = parameters.get("purpose") ?? "";
  useEffect(() => {
    if (started.current) return;
    started.current = true;
    void (async () => {
      const configuration = await request<ProductIntegrationConfiguration>("/integration/product/configuration");
      if (!configuration.enabled) throw new Error("The product workspace integration is not enabled.");
      const result = await post<Handoff>("/integration/product/handoff", { role, purpose, callbackId: configuration.callbackId, state });
      submitForm(result.callbackUrl, { code: result.code, state: result.state, callbackId: configuration.callbackId });
    })().catch((reason) => setError(reason instanceof Error ? reason.message : "We couldn't open your workspace."));
  }, [purpose, role, state]);
  return <main className="loading product-handoff-state" role="status">{error ? <><Notice error>{error}</Notice><Button onClick={() => location.assign("/onboarding")}>Back to profiles</Button></> : <p>Opening your Weymela workspace…</p>}</main>;
}

export function ProductSignOut() {
  const { signOut } = useSession();
  const navigate = useNavigate();
  const started = useRef(false);
  useEffect(() => {
    if (started.current) return;
    started.current = true;
    void signOut().finally(() => navigate("/sign-in", { replace: true }));
  }, [navigate, signOut]);
  return <main className="loading" role="status">Signing out…</main>;
}

export function isPublicProductRole(role: Role): role is PublicRole {
  return role === "Customer" || role === "Creator" || role === "Business" || role === "PlatformAdmin";
}
