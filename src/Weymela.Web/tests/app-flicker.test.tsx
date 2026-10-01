import { afterEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, useLocation, useNavigate } from "react-router-dom";
import type { Role, SessionUser } from "../src/api/types";
import { App } from "../src/app/App";
import { SessionProvider, useSession } from "../src/app/Session";
import { invalidateResourceCache, primeResources } from "../src/api/client";

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
    if (input === "/api/customer/offers" || input === "/api/customer/transactions" || input === "/api/checkout/recent"
        || input === "/api/creator/campaigns" || input === "/api/creator/discover"
        || input === "/api/creator/ugc") return Promise.resolve(Response.json([]));
    if (input === "/api/customer/cashback") return Promise.resolve(Response.json({ availableCashback: { amount: 0 }, status: "BelowMinimum" }));
    if (input === "/api/creator/home") return Promise.resolve(Response.json({ creator: { displayName: "Creator" },
      requests: 0, activeCampaigns: 0, earnings: { availableEarnings: 0, history: [], payoutHistory: [] } }));
    if (input === "/api/notifications") return Promise.resolve(Response.json({ items: [], unreadCount: 0 }));
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
  it("drops an in-flight destination response after the profile context changes", async () => {
    let completeFirst!: (response: Response) => void;
    const fetch = vi.fn()
      .mockImplementationOnce(() => new Promise<Response>(resolve => { completeFirst = resolve; }))
      .mockResolvedValue(Response.json([{ platform: "YouTube" }]));
    vi.stubGlobal("fetch", fetch);
    window.sessionStorage.setItem("weymela.profile-key", "Creator:first");
    const first = primeResources(["/creator/social-accounts"]);
    await waitFor(() => expect(completeFirst).toBeDefined());
    window.sessionStorage.setItem("weymela.profile-key", "Creator:second");
    invalidateResourceCache(false);
    completeFirst(Response.json([{ platform: "TikTok" }]));
    await expect(first).rejects.toThrow("Workspace profile changed during navigation.");
    await primeResources(["/creator/social-accounts"]);
    expect(fetch).toHaveBeenCalledTimes(2);
    invalidateResourceCache(false);
  });

  it("retains the current Creator page until first-visit destination data is ready", async () => {
    const creator: SessionUser = { role: "Creator", displayName: "Bela", publicId: "CR-1",
      developmentMode: true, canCheckout: false, activeProfileKey: "Creator:creator-1:-", profiles: [] };
    let finishDiscover!: (response: Response) => void;
    vi.stubGlobal("fetch", vi.fn((input: RequestInfo | URL) => {
      if (input === "/api/device/access") return Promise.resolve(Response.json({ state: "Unlocked", idleExpiresAtUtc: null }));
      if (input === "/api/session") return Promise.resolve(Response.json(creator));
      if (input === "/api/device/enrollment") return Promise.resolve(Response.json({ state: "Enrolled", expiresAtUtc: null }));
      if (input === "/api/creator/home") return Promise.resolve(Response.json({ creator: { displayName: "Bela" }, requests: 1,
        activeCampaigns: 0, earnings: { availableEarnings: 0, history: [], payoutHistory: [] } }));
      if (input === "/api/creator/campaigns" || input === "/api/creator/ugc") return Promise.resolve(Response.json([]));
      if (input === "/api/creator/discover") return new Promise<Response>(resolve => { finishDiscover = resolve; });
      return Promise.resolve(Response.json([]));
    }));
    renderApp("/creator");
    const mainNavigation = await waitFor(() => {
      const navigation = document.querySelector('nav[aria-label="Main navigation"]');
      if (!navigation) throw new Error("Creator navigation is not ready.");
      return navigation;
    });
    const homeLink = await waitFor(() => {
      const link = mainNavigation.querySelector('a[href="/creator"]');
      if (!link) throw new Error("Creator home link is not ready.");
      return link;
    });
    const shell = document.querySelector(".app-shell");
    await userEvent.click(screen.getByRole("navigation", { name: "Main navigation" }).querySelector('a[href="/creator/discover"]')!);
    await waitFor(() => expect(finishDiscover).toBeDefined());
    expect(mainNavigation.querySelector('a[href="/creator"]')).toBe(homeLink);
    expect(screen.getByLabelText("Path")).toHaveTextContent("/creator");
    expect(screen.queryByRole("status", { name: "Loading workspace" })).not.toBeInTheDocument();
    await act(async () => finishDiscover(Response.json([])));
    await waitFor(() => expect(screen.getByLabelText("Path")).toHaveTextContent("/creator/discover"));
    expect(document.querySelector(".app-shell")).toBe(shell);
    expect(screen.queryByRole("status", { name: "Loading workspace" })).not.toBeInTheDocument();
  });
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
