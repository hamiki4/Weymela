import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../src/api/client";

const mocks = vi.hoisted(() => ({
  request: vi.fn(),
  firebaseSignOut: vi.fn(),
}));

vi.mock("../src/api/client", async (original) => {
  const actual = await original<typeof import("../src/api/client")>();
  return { ...actual, request: mocks.request, post: vi.fn() };
});
vi.mock("../src/auth/firebase", () => ({
  FirebaseConfigurationError: class extends Error {},
  createFirebaseWebAuthAdapter: () => ({ signOut: mocks.firebaseSignOut }),
}));

import { SessionProvider, useSession } from "../src/app/Session";

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}

const access = {
  state: "Unlocked",
  idleExpiresAtUtc: null,
  sessionExpiresAtUtc: null,
  retryAfterSeconds: null,
};
const user = {
  role: "Customer",
  displayName: "Account",
  publicId: "",
  developmentMode: false,
  canCheckout: false,
  profiles: [],
  activeProfileKey: null,
};

function Observer() {
  const session = useSession();
  return <>
    <output aria-label="session-state">
      {session.loading ? "loading" : session.loadFailed ? "failed" : session.user ? "authenticated" : "anonymous"}
    </output>
    <button type="button" onClick={() => void session.refresh()}>Refresh</button>
    <button type="button" onClick={() => void session.signOut()}>Sign out</button>
  </>;
}

function renderSession() {
  return render(<MemoryRouter><SessionProvider><Observer /></SessionProvider></MemoryRouter>);
}

function authenticatedRequests(path: string) {
  if (path === "/device/access") return Promise.resolve(access);
  if (path === "/session") return Promise.resolve(user);
  if (path === "/account/security") return Promise.resolve({ passwordEnrolled: true, phoneEnrolled: true });
  if (path === "/device/enrollment") return Promise.resolve({ state: "Enrolled", expiresAtUtc: null });
  return Promise.reject(new Error(`Unexpected request: ${path}`));
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.firebaseSignOut.mockResolvedValue(undefined);
  window.sessionStorage.clear();
});

describe("authoritative session refresh ordering", () => {
  it("does not let an older unauthenticated completion clear a newer authenticated refresh", async () => {
    const older = deferred<typeof access>();
    let accessCalls = 0;
    mocks.request.mockImplementation((path: string) => {
      if (path === "/device/access" && accessCalls++ === 0) return older.promise;
      return authenticatedRequests(path);
    });
    renderSession();
    await waitFor(() => expect(mocks.request).toHaveBeenCalledTimes(1));
    await userEvent.click(screen.getByRole("button", { name: "Refresh" }));
    expect(await screen.findByLabelText("session-state")).toHaveTextContent("authenticated");

    await act(async () => older.reject(new ApiError(401, "Sign in to continue.")));
    expect(screen.getByLabelText("session-state")).toHaveTextContent("authenticated");
  });

  it("does not let a stale authenticated refresh overwrite a newer authoritative anonymous result", async () => {
    const older = deferred<typeof access>();
    let accessCalls = 0;
    mocks.request.mockImplementation((path: string) => {
      if (path === "/device/access" && accessCalls++ === 0) return older.promise;
      if (path === "/device/access") return Promise.reject(new ApiError(401, "Sign in to continue."));
      return authenticatedRequests(path);
    });
    renderSession();
    await waitFor(() => expect(mocks.request).toHaveBeenCalledTimes(1));
    await userEvent.click(screen.getByRole("button", { name: "Refresh" }));
    await waitFor(() => expect(screen.getByLabelText("session-state")).toHaveTextContent("anonymous"));

    await act(async () => older.resolve(access));
    expect(screen.getByLabelText("session-state")).toHaveTextContent("anonymous");
  });

  it("reports an authoritative bootstrap failure instead of retaining authenticated state", async () => {
    mocks.request.mockRejectedValueOnce(new ApiError(503, "Unavailable"));
    renderSession();
    await waitFor(() => expect(screen.getByLabelText("session-state")).toHaveTextContent("failed"));
  });

  it("invalidates an in-flight refresh when the user intentionally signs out", async () => {
    mocks.request.mockImplementation(authenticatedRequests);
    renderSession();
    await waitFor(() => expect(screen.getByLabelText("session-state")).toHaveTextContent("authenticated"));

    const stale = deferred<typeof access>();
    mocks.request.mockImplementation((path: string) => path === "/device/access" ? stale.promise : authenticatedRequests(path));
    await userEvent.click(screen.getByRole("button", { name: "Refresh" }));
    await userEvent.click(screen.getByRole("button", { name: "Sign out" }));
    await waitFor(() => expect(screen.getByLabelText("session-state")).toHaveTextContent("anonymous"));
    await act(async () => stale.resolve(access));
    expect(screen.getByLabelText("session-state")).toHaveTextContent("anonymous");
  });

  it("keeps logout authoritative over a refresh started while sign-out is in progress", async () => {
    mocks.request.mockImplementation(authenticatedRequests);
    renderSession();
    await waitFor(() => expect(screen.getByLabelText("session-state")).toHaveTextContent("authenticated"));

    const signOut = deferred<void>();
    const refreshDuringLogout = deferred<typeof access>();
    mocks.firebaseSignOut.mockReturnValueOnce(signOut.promise);
    mocks.request.mockImplementation((path: string) =>
      path === "/device/access" ? refreshDuringLogout.promise : authenticatedRequests(path));

    await userEvent.click(screen.getByRole("button", { name: "Sign out" }));
    await userEvent.click(screen.getByRole("button", { name: "Refresh" }));
    await act(async () => signOut.resolve());
    await waitFor(() => expect(screen.getByLabelText("session-state")).toHaveTextContent("anonymous"));

    await act(async () => refreshDuringLogout.resolve(access));
    expect(screen.getByLabelText("session-state")).toHaveTextContent("anonymous");
  });
});
