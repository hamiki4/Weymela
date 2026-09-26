import { afterEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, useLocation, useNavigate } from "react-router-dom";
import type { Role, SessionUser } from "../src/api/types";
import { App } from "../src/app/App";
import { SessionProvider, useSession } from "../src/app/Session";

function NavigationControls() {
  const navigate = useNavigate();
  const location = useLocation();
  const session = useSession();
  return <>
    <output aria-label="Path">{location.pathname}</output>
    <button onClick={() => navigate(-1)}>Back</button>
    <button onClick={() => navigate(1)}>Forward</button>
    <button onClick={() => void session.refresh()}>Refresh session</button>
  </>;
}

function renderApp(path: string) {
  return render(<MemoryRouter initialEntries={[path]}><SessionProvider><NavigationControls /><App /></SessionProvider></MemoryRouter>);
}

function sessionResponses(role: Role) {
  const user: SessionUser = {
    role, displayName: `${role} member`, publicId: `${role}-1`, developmentMode: true,
    canCheckout: role === "Business" || role === "Cashier", activeProfileKey: `${role}:${role}-1:-`, profiles: [],
  };
  const fetch = vi.fn((input: RequestInfo | URL) => {
    if (input === "/api/device/access") return Promise.resolve(Response.json({
      state: "Unlocked", idleExpiresAtUtc: null, sessionExpiresAtUtc: null, retryAfterSeconds: null,
    }));
    if (input === "/api/session") return Promise.resolve(Response.json(user));
    if (input === "/api/device/enrollment") return Promise.resolve(Response.json({ state: "Enrolled", expiresAtUtc: null }));
    // Hold first-visit page requests to exercise the shared layout during slow loading.
    return new Promise<Response>(() => {});
  });
  vi.stubGlobal("fetch", fetch);
  return fetch;
}

afterEach(() => {
  vi.unstubAllGlobals();
  window.sessionStorage.clear();
});

describe("shared app navigation", () => {
  it.each([
    ["Customer", "/customer/offers", "/customer/discover"],
    ["Creator", "/creator", "/creator/discover"],
    ["Business", "/business", "/checkout"],
    ["Cashier", "/checkout", "/notifications"],
    ["OperationsAdmin", "/admin/operations", "/admin/role-enrollments"],
    ["PlatformAdmin", "/admin", "/admin/customers"],
  ] as const)("keeps the %s shell mounted across navigation and history", async (role, from, to) => {
    sessionResponses(role);
    renderApp(from);
    const shell = await waitFor(() => {
      const element = document.querySelector(".app-shell");
      expect(element).not.toBeNull();
      return element;
    });
    const user = userEvent.setup();
    const link = screen.getByRole("navigation", { name: "Main navigation" }).querySelector(`a[href="${to}"]`);
    if (link) await user.click(link);
    else await user.click(screen.getByRole("link", { name: "Your notifications" }));
    await waitFor(() => expect(screen.getByLabelText("Path")).toHaveTextContent(to));
    expect(document.querySelector(".app-shell")).toBe(shell);
    expect(screen.queryByText("Preparing your secure session…")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Back" }));
    await waitFor(() => expect(screen.getByLabelText("Path")).toHaveTextContent(from));
    expect(document.querySelector(".app-shell")).toBe(shell);
    await user.click(screen.getByRole("button", { name: "Forward" }));
    await waitFor(() => expect(screen.getByLabelText("Path")).toHaveTextContent(to));
    expect(document.querySelector(".app-shell")).toBe(shell);
  });

  it("shows the authentication shell during a slow initial check without exposing Sign In or a workspace", async () => {
    let resolveAccess!: (response: Response) => void;
    const fetch = vi.fn((input: RequestInfo | URL) => input === "/api/device/access"
      ? new Promise<Response>(resolve => { resolveAccess = resolve; })
      : input === "/api/auth/mode"
        ? Promise.resolve(Response.json({ development: true, personas: [] }))
        : new Promise<Response>(() => {}));
    vi.stubGlobal("fetch", fetch);
    renderApp("/sign-in");
    const shell = document.querySelector(".sign-in");
    expect(screen.getByRole("img", { name: "Weymela" })).toBeVisible();
    expect(screen.getByText("Preparing your secure session…")).toHaveAttribute("role", "status");
    expect(screen.queryByRole("heading", { name: /Sign In|Welcome/ })).not.toBeInTheDocument();
    expect(document.querySelector(".app-shell")).toBeNull();
    resolveAccess(Response.json({}, { status: 401 }));
    expect(await screen.findByRole("heading", { name: "Welcome to Weymela" })).toBeVisible();
    expect(document.querySelector(".sign-in")).toBe(shell);
    expect(screen.queryByRole("status", { name: "Loading workspace" })).not.toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith("/api/auth/mode", expect.anything());
  });

  it("retains an authorized page during slow revalidation and removes it after a failed refresh", async () => {
    let accessCalls = 0;
    let finishRefresh!: (response: Response) => void;
    const customer: SessionUser = { role: "Customer", displayName: "Hana", publicId: "CU-1",
      developmentMode: true, canCheckout: false, activeProfileKey: "Customer:customer-1:-", profiles: [] };
    vi.stubGlobal("fetch", vi.fn((input: RequestInfo | URL) => {
      if (input === "/api/device/access") return ++accessCalls === 1
        ? Promise.resolve(Response.json({ state: "Unlocked", idleExpiresAtUtc: null }))
        : new Promise<Response>(resolve => { finishRefresh = resolve; });
      if (input === "/api/session") return Promise.resolve(Response.json(customer));
      if (input === "/api/device/enrollment") return Promise.resolve(Response.json({ state: "Enrolled", expiresAtUtc: null }));
      if (input === "/api/customer/offers") return Promise.resolve(Response.json([]));
      if (input === "/api/customer/cashback") return Promise.resolve(Response.json({ availableCashback: { amount: 0 }, status: "BelowMinimum" }));
      if (input === "/api/customer/transactions") return Promise.resolve(Response.json([]));
      if (input === "/api/auth/mode") return Promise.resolve(Response.json({ development: true, personas: [] }));
      return new Promise<Response>(() => {});
    }));
    renderApp("/customer/offers");
    const heading = await screen.findByRole("heading", { name: "Home" });
    await waitFor(() => expect(screen.queryByRole("status", { name: "Loading workspace" })).not.toBeInTheDocument());
    const shell = document.querySelector(".app-shell");
    await userEvent.click(screen.getByRole("button", { name: "Refresh session" }));
    await waitFor(() => expect(finishRefresh).toBeDefined());
    expect(screen.getByRole("heading", { name: "Home" })).toBe(heading);
    expect(document.querySelector(".app-shell")).toBe(shell);
    expect(screen.queryByText("Checking your secure session…")).not.toBeInTheDocument();
    expect(screen.queryByRole("status", { name: "Loading workspace" })).not.toBeInTheDocument();
    await act(async () => finishRefresh(Response.json({}, { status: 401 })));
    await waitFor(() => expect(screen.getByLabelText("Path")).toHaveTextContent("/sign-in"));
    expect(screen.queryByRole("heading", { name: "Home" })).not.toBeInTheDocument();
  });

  it("opens PIN setup directly after profile and device resolution", async () => {
    const customer: SessionUser = { role: "Customer", displayName: "Hana", publicId: "CU-1",
      developmentMode: true, canCheckout: false, activeProfileKey: "Customer:customer-1:-", profiles: [] };
    vi.stubGlobal("fetch", vi.fn((input: RequestInfo | URL) => {
      if (input === "/api/device/access") return Promise.resolve(Response.json({ state: "EnrollmentRequired" }));
      if (input === "/api/session") return Promise.resolve(Response.json(customer));
      if (input === "/api/device/enrollment") return Promise.resolve(Response.json({ state: "EnrollmentRequired", expiresAtUtc: null }));
      return new Promise<Response>(() => {});
    }));
    renderApp("/sign-in");
    expect(await screen.findByRole("heading", { name: "Create your PIN" })).toBeVisible();
    expect(screen.getByLabelText("Path")).toHaveTextContent("/pin-setup");
    expect(screen.queryByText("Opening your account…")).not.toBeInTheDocument();
    expect(document.querySelector(".app-shell")).toBeNull();
  });

  it("keeps PIN unlock visible until the refreshed session authorizes the workspace", async () => {
    let accessCalls = 0;
    let completeAccess!: (response: Response) => void;
    const cashier: SessionUser = { role: "Cashier", displayName: "Cashier", publicId: "CA-1",
      developmentMode: true, canCheckout: true, activeProfileKey: "Cashier:cashier-1:-", profiles: [] };
    vi.stubGlobal("fetch", vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      if (input === "/api/device/access") return ++accessCalls === 1
        ? Promise.resolve(Response.json({ state: "Locked", retryAfterSeconds: null }))
        : new Promise<Response>(resolve => { completeAccess = resolve; });
      if (input === "/api/device/unlock" && init?.method === "POST")
        return Promise.resolve(Response.json({ state: "Unlocked" }));
      if (input === "/api/session") return Promise.resolve(Response.json(cashier));
      if (input === "/api/device/enrollment") return Promise.resolve(Response.json({ state: "NotRequired", expiresAtUtc: null }));
      return new Promise<Response>(() => {});
    }));
    renderApp("/checkout");
    expect(await screen.findByRole("heading", { name: "Welcome back" })).toBeVisible();
    const pin = screen.getByRole("group", { name: "PIN" }).querySelector("input")!;
    await userEvent.type(pin, "01234");
    await userEvent.click(screen.getByRole("button", { name: "Unlock" }));
    await waitFor(() => expect(completeAccess).toBeDefined());
    expect(screen.getByRole("heading", { name: "Welcome back" })).toBeVisible();
    expect(screen.queryByText("Preparing your secure session…")).not.toBeInTheDocument();
    expect(document.querySelector(".app-shell")).toBeNull();
    await act(async () => completeAccess(Response.json({ state: "Unlocked", idleExpiresAtUtc: null })));
    await waitFor(() => expect(document.querySelector(".app-shell")).not.toBeNull());
    expect(screen.getByLabelText("Path")).toHaveTextContent("/checkout");
  });
});
