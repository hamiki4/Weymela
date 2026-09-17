import { useEffect, useRef, useState } from "react";
import { Navigate, useNavigate, useSearchParams } from "react-router-dom";
import { post, request, useResource } from "../api/client";
import type { Role } from "../api/types";
import { Button } from "../ui/components";
import { useSession } from "./Session";

type PublicRole = "Customer" | "Creator" | "Business" | "PlatformAdmin";
type Purpose = "PROFILE_ONBOARDING" | "EXISTING_WORKSPACE";
export type ProductIntegrationConfiguration = { enabled: boolean; beginUrl: string; callbackId: string };
type Handoff = { code: string; state: string; callbackUrl: string; expiresAtUtc: string };
type HandoffBegin = { state: string };
type HandoffCompletion = { destination: string };

async function postHandoffForm<T>(action: string, fields: Record<string, string>): Promise<T> {
  const target = new URL(action, location.origin);
  if (target.origin !== location.origin)
    throw new Error("The product workspace destination is not trusted.");
  const response = await fetch(target, {
    method: "POST",
    credentials: "same-origin",
    cache: "no-store",
    redirect: "error",
    headers: {
      "Accept": "application/json",
      "Content-Type": "application/x-www-form-urlencoded;charset=UTF-8",
      "X-Weymela-Product-Request": "1",
    },
    body: new URLSearchParams(fields),
  });
  if (!response.ok) throw new Error("We couldn't open your workspace.");
  return response.json() as Promise<T>;
}

function expectedDestination(role: PublicRole, purpose: Purpose) {
  if (purpose === "PROFILE_ONBOARDING") {
    if (role === "PlatformAdmin") throw new Error("The Admin workspace request is not valid.");
    return `/onboarding/${role === "Customer" ? "customer" : role === "Creator" ? "creator" : "business"}`;
  }
  return role === "Customer" ? "/shopper" : role === "Creator" ? "/creator"
    : role === "Business" ? "/business" : "/admin";
}

async function completeProductHandoff(role: PublicRole, purpose: Purpose, state: string,
    configuration: ProductIntegrationConfiguration) {
  const result = await post<Handoff>("/integration/product/handoff", {
    role, purpose, callbackId: configuration.callbackId, state,
  });
  const completion = await postHandoffForm<HandoffCompletion>(result.callbackUrl, {
    code: result.code, state: result.state, callbackId: configuration.callbackId,
  });
  const expected = expectedDestination(role, purpose);
  if (completion.destination !== expected)
    throw new Error("The product workspace destination is not valid.");
  location.replace(expected);
}

export async function beginProductHandoff(role: PublicRole, purpose: Purpose,
    suppliedConfiguration?: ProductIntegrationConfiguration) {
  const configuration = suppliedConfiguration
    ?? await request<ProductIntegrationConfiguration>("/integration/product/configuration");
  if (!configuration.enabled) throw new Error("The product workspace integration is not enabled.");
  const started = await postHandoffForm<HandoffBegin>(configuration.beginUrl, { role, purpose });
  await completeProductHandoff(role, purpose, started.state, configuration);
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
    void beginProductHandoff(role, "EXISTING_WORKSPACE", configuration.data ?? undefined).catch((reason) => {
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
  return error
    ? <main className="loading product-handoff-state" role="status"><p>{error}</p><Button onClick={begin}>Try again</Button></main>
    : <main className="loading product-handoff-state" aria-busy="true" />;
}

export function ProductHandoffCallback() {
  const [parameters] = useSearchParams();
  const [error, setError] = useState("");
  const started = useRef(false);
  const state = parameters.get("state") ?? "";
  const role = parameters.get("role") ?? "";
  const purpose = parameters.get("purpose") ?? "";
  const validRole = isPublicProductRole(role as Role);
  const validPurpose = purpose === "PROFILE_ONBOARDING" || purpose === "EXISTING_WORKSPACE";
  const recovery = productHandoffFailureRecovery(role);
  useEffect(() => {
    if (started.current) return;
    if (!state || !validRole || !validPurpose) {
      location.replace(recovery.path);
      return;
    }
    started.current = true;
    void (async () => {
      const configuration = await request<ProductIntegrationConfiguration>("/integration/product/configuration");
      if (!configuration.enabled) throw new Error("The product workspace integration is not enabled.");
      await completeProductHandoff(role as PublicRole, purpose as Purpose, state, configuration);
    })().catch(() => {
      setError("handoff-failed");
      location.replace(recovery.path);
    });
  }, [purpose, recovery.path, role, state, validPurpose, validRole]);
  return error ? <Navigate to={recovery.path} replace />
    : <main className="loading product-handoff-state" aria-busy="true" />;
}

export function productHandoffFailureRecovery(role: string) {
  return role === "PlatformAdmin"
    ? { label: "Retry Admin workspace", path: "/admin" }
    : { label: "Back to profiles", path: "/onboarding" };
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
