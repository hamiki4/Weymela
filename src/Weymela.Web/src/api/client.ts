import { useCallback, useEffect, useRef, useState } from "react";
import { validateWalletContract } from "./walletContract";

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
    public code?: string,
    public retryAfterSeconds?: number | null,
  ) {
    super(message);
  }
}

const resourceContextChangedEvent = "weymela-resource-context-changed";
const resourceCache = new Map<string, unknown>();
let resourceContextGeneration = 0;

function clearResourceCache() {
  resourceCache.clear();
  resourceContextGeneration += 1;
}

/**
 * Clears in-memory workspace data when the server-authoritative session
 * context changes. This cache is intentionally neither persisted nor used for
 * authorization; it only lets an already-authorized page revalidate without
 * flashing an empty skeleton on ordinary navigation.
 */
export function invalidateResourceCache(notifyMountedResources = true) {
  clearResourceCache();
  if (notifyMountedResources && typeof window !== "undefined")
    window.dispatchEvent(new Event(resourceContextChangedEvent));
}

function cachedResource<T>(path: string): { path: string; value: T; generation: number } | null {
  return resourceCache.has(path)
    ? { path, value: resourceCache.get(path) as T, generation: resourceContextGeneration }
    : null;
}

export async function request<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const response = await fetch(`/api${path}`, {
    credentials: "same-origin",
    cache: "no-store",
    ...options,
    headers: {
      "Content-Type": "application/json",
      "X-Weymela-Request": "1",
      ...(typeof window !== "undefined" && window.sessionStorage.getItem("weymela.profile-key")
        ? { "X-Weymela-Profile": window.sessionStorage.getItem("weymela.profile-key")! }
        : {}),
      ...options.headers,
    },
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    const apiError = new ApiError(
      response.status,
      error.message ??
        (error.code === "ProfileContextChanged"
          ? "This workspace changed in another tab. Refresh before submitting again."
          : response.status === 401
          ? "Sign in to continue."
          : response.status === 403
            ? "You do not have access to this workspace."
            : "We could not complete that request. Please try again."),
      error.code,
      error.retryAfterSeconds,
    );
    if (typeof window !== "undefined" && ["SessionLocked", "PinCooldown", "PinRecoveryRequired", "FullAuthenticationRequired"].includes(error.code)) {
      window.dispatchEvent(new CustomEvent("weymela-device-access", { detail: { state: error.state ?? error.code } }));
      if (typeof BroadcastChannel !== "undefined") {
        const channel = new BroadcastChannel("weymela-v3-access");
        channel.postMessage(error.code === "FullAuthenticationRequired" ? "full-authentication-required" : "locked");
        channel.close();
      }
    }
    throw apiError;
  }
  if (response.status === 204) return undefined as T;
  const body = await response.text();
  const payload = body ? JSON.parse(body) : null;
  if (path === "/business/home" || path === "/business/wallet") validateWalletContract(payload);
  return payload as T;
}
export function post<T = { id: string }>(
  path: string,
  data?: unknown,
  key: string = crypto.randomUUID(),
): Promise<T> {
  return request<T>(path, {
    method: "POST",
    headers: { "Idempotency-Key": key, "X-Weymela-Activity": "1" },
    body: data === undefined ? undefined : JSON.stringify(data),
  });
}
export function useResource<T>(path: string) {
  const [saved, setData] = useState<{ path: string; value: T; generation: number } | null>(() => cachedResource<T>(path));
  const [error, setError] = useState<Error | null>(null);
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);
  const reload = useCallback(() => setRevision((x) => x + 1), []);
  useEffect(() => {
    const abort = new AbortController();
    const requestContextGeneration = resourceContextGeneration;
    setData(cachedResource<T>(path));
    setLoading(true);
    setError(null);
    request<T>(path, { signal: abort.signal })
      .then((value) => {
        if (!abort.signal.aborted && requestContextGeneration === resourceContextGeneration) {
          resourceCache.set(path, value);
          setData({ path, value, generation: requestContextGeneration });
        }
      })
      .catch((e: Error) => {
        if (!abort.signal.aborted && requestContextGeneration === resourceContextGeneration) setError(e);
      })
      .finally(() => {
        if (!abort.signal.aborted && requestContextGeneration === resourceContextGeneration) setLoading(false);
      });
    return () => abort.abort();
  }, [path, revision]);
  useEffect(() => {
    const refreshForContext = () => {
      setData(null);
      setError(null);
      setRevision((x) => x + 1);
    };
    window.addEventListener(resourceContextChangedEvent, refreshForContext);
    return () => {
      window.removeEventListener(resourceContextChangedEvent, refreshForContext);
    };
  }, []);
  const data = saved?.path === path && saved.generation === resourceContextGeneration ? saved.value : null;
  return { data, error, loading: loading || (data === null && !error), reload };
}
export function useAction() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const key = useRef(crypto.randomUUID());
  const running = useRef(false);
  const run = async (action: (key: string) => Promise<void>) => {
    if (running.current) return;
    if (!navigator.onLine) {
      setError(
        "You’re offline. Reconnect before submitting this action. Nothing has been queued.",
      );
      return;
    }
    running.current = true;
    setBusy(true);
    setError(null);
    try {
      await action(key.current);
      key.current = crypto.randomUUID();
    } catch (e) {
      setError(
        e instanceof Error ? e.message : "We could not complete that request.",
      );
    } finally {
      running.current = false;
      setBusy(false);
    }
  };
  return { busy, error, run };
}
