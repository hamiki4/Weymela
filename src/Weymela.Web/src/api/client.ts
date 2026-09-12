import { useCallback, useEffect, useRef, useState } from "react";

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
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
      ...options.headers,
    },
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new ApiError(
      response.status,
      error.message ??
        (response.status === 401
          ? "Sign in to continue."
          : response.status === 403
            ? "You do not have access to this workspace."
            : "We could not complete that request. Please try again."),
    );
  }
  return response.status === 204
    ? (undefined as T)
    : (response.json() as Promise<T>);
}
export function post<T = { id: string }>(
  path: string,
  data?: unknown,
  key: string = crypto.randomUUID(),
): Promise<T> {
  return request<T>(path, {
    method: "POST",
    headers: { "Idempotency-Key": key },
    body: data === undefined ? undefined : JSON.stringify(data),
  });
}
export function useResource<T>(path: string) {
  const [saved, setData] = useState<{ path: string; value: T } | null>(null);
  const [error, setError] = useState<Error | null>(null);
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);
  const reload = useCallback(() => setRevision((x) => x + 1), []);
  useEffect(() => {
    const abort = new AbortController();
    setLoading(true);
    setError(null);
    request<T>(path, { signal: abort.signal })
      .then((value) => {
        if (!abort.signal.aborted) setData({ path, value });
      })
      .catch((e: Error) => {
        if (!abort.signal.aborted) setError(e);
      })
      .finally(() => {
        if (!abort.signal.aborted) setLoading(false);
      });
    return () => abort.abort();
  }, [path, revision]);
  const data = saved?.path === path ? saved.value : null;
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
