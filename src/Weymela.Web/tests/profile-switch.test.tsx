import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useLayoutEffect, useState } from "react";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BrowserRouter, Link, Route, Routes, useLocation } from "react-router-dom";
import type { Role, SessionProfile, SessionUser } from "../src/api/types";
import { ProfileSwitcher } from "../src/app/Shell";
import { RoleGate, SessionProvider, roleHome, useSession } from "../src/app/Session";

const profiles: SessionProfile[] = [
  { role: "Customer", subjectId: "customer-1", businessId: null, displayName: "Customer", publicId: "CU-1", canCheckout: false },
  { role: "Creator", subjectId: "creator-1", businessId: null, displayName: "Bella", publicId: "CR-1", canCheckout: false },
  { role: "Business", subjectId: "business-1", businessId: "business-1", displayName: "ABC Café", publicId: "BUS-1", canCheckout: true },
];

const profileKey = (profile: SessionProfile) => `${profile.role}:${profile.subjectId}:${profile.businessId ?? "-"}`;
function sessionFor(profile: SessionProfile): SessionUser {
  return { ...profile, profiles, developmentMode: false, activeProfileKey: profileKey(profile) };
}

beforeEach(() => {
  window.history.replaceState(null, "", "/customer/offers");
  window.sessionStorage.clear();
});
afterEach(() => vi.unstubAllGlobals());

describe("approved profile selector", () => {
  it("shows only approved profiles and requests a server switch for the selected membership", async () => {
    const onSwitch = vi.fn(async (profile: SessionProfile) => ({ role: profile.role }));
    render(<ProfileSwitcher profiles={profiles} activeKey="Creator:creator-1:-" onSwitch={onSwitch} />);
    expect(screen.getByLabelText("Switch profile")).toBeVisible();
    await userEvent.selectOptions(screen.getByLabelText("Switch profile"), "2");
    expect(onSwitch).toHaveBeenCalledTimes(1);
    expect(onSwitch).toHaveBeenCalledWith(profiles[2]);
    expect(screen.queryByRole("option", { name: /Platform Admin|Cashier/ })).not.toBeInTheDocument();
  });
});

// Only HTTP is stubbed here. The real BrowserRouter, SessionProvider, RoleGate
// and ProfileSwitcher must commit a consistent role/location together.
function sessionServer(switchStatus = 200) {
  let current = sessionFor(profiles[0]);
  const now = Date.now();
  const accessStatus = {
    state: "Unlocked",
    idleExpiresAtUtc: new Date(now + 20 * 60 * 1000).toISOString(),
    sessionExpiresAtUtc: new Date(now + 60 * 60 * 1000).toISOString(),
    retryAfterSeconds: null,
  };
  const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    if (input === "/api/device/access" && !init?.method)
      return Response.json(accessStatus);
    if (input === "/api/session" && !init?.method) return Response.json(current);
    if (input === "/api/account/security" && !init?.method)
      return Response.json({ passwordEnrolled: true, phoneEnrolled: true });
    if (input === "/api/device/enrollment" && !init?.method)
      return Response.json({ state: "Enrolled", expiresAtUtc: "2026-10-14T00:00:00Z" });
    if (input === "/api/session/switch-profile" && init?.method === "POST") {
      if (switchStatus !== 200) return Response.json({ message: "Switch rejected." }, { status: switchStatus });
      const requested = JSON.parse(String(init.body)) as SessionProfile;
      const selected = profiles.find(profile => profile.role === requested.role
        && profile.subjectId === requested.subjectId && profile.businessId === requested.businessId);
      if (!selected) return Response.json({ message: "No approved membership." }, { status: 403 });
      current = sessionFor(selected);
      return Response.json(current);
    }
    throw new Error(`Unexpected test request: ${String(input)}`);
  });
  vi.stubGlobal("fetch", fetch);
  return fetch;
}

type CommittedContext = { role: Role | undefined; path: string };
function ContextProbe({ commits }: { commits: CommittedContext[] }) {
  const { user, loading } = useSession();
  const { pathname } = useLocation();
  useLayoutEffect(() => {
    if (!loading) commits.push({ role: user?.role, path: pathname });
  }, [user, loading, pathname, commits]);
  return <output aria-label="Active role">{user?.role}</output>;
}

function TestWorkspace() {
  const { user, switchProfile } = useSession();
  const [rejected, setRejected] = useState(false);
  if (!user) return null;
  return <main>
    <h1>{user.role} workspace</h1>
    <ProfileSwitcher profiles={user.profiles ?? []} activeKey={user.activeProfileKey} onSwitch={switchProfile} />
    <Link to="/creator">Open Creator directly</Link>
    <button onClick={() => void switchProfile({ ...profiles[1], subjectId: "unapproved-creator" }).catch(() => setRejected(true))}>
      Attempt tampered profile
    </button>
    {rejected && <p role="alert">Unapproved switch rejected</p>}
  </main>;
}

function SessionControls() {
  const session = useSession();
  return <>
    <button onClick={() => void session.refresh()}>Refresh session</button>
    <button onClick={() => void session.switchProfile(profiles[1])}>Switch to Creator</button>
  </>;
}

function renderSessionRoutes() {
  const commits: CommittedContext[] = [];
  render(<BrowserRouter><SessionProvider>
    <ContextProbe commits={commits} />
    <SessionControls />
    <Routes>
      {(["Customer", "Creator", "Business"] as const).map(role =>
        <Route key={role} path={`${roleHome[role]}/*`} element={<RoleGate roles={[role]}><TestWorkspace /></RoleGate>} />)}
      <Route path="/unauthorized" element={<h1>Workspace unavailable</h1>} />
    </Routes>
  </SessionProvider></BrowserRouter>);
  return commits;
}

describe("profile switching with the real router and session guard", () => {
  it("ignores a previous-profile session response after an authorized switch", async () => {
    const fetch = sessionServer();
    renderSessionRoutes();
    await screen.findByRole("heading", { name: "Customer workspace" });
    const original = fetch.getMockImplementation()!;
    let release!: (value: Response) => void;
    fetch.mockImplementation((input, init) => input === "/api/session" && !init?.method
      ? new Promise<Response>(resolve => { release = resolve; })
      : original(input, init));
    await userEvent.click(screen.getByRole("button", { name: "Refresh session" }));
    await waitFor(() => expect(release).toBeDefined());
    await userEvent.click(screen.getByRole("button", { name: "Switch to Creator" }));
    expect(await screen.findByRole("heading", { name: "Creator workspace" })).toBeVisible();
    await act(async () => release(Response.json(sessionFor(profiles[0]))));
    expect(screen.getByRole("heading", { name: "Creator workspace" })).toBeVisible();
    expect(window.sessionStorage.getItem("weymela.profile-key")).toBe(profileKey(profiles[1]));
  });

  it("commits Customer, Creator and Business switches without any unauthorized or mismatched route render", async () => {
    const fetch = sessionServer();
    const commits = renderSessionRoutes();
    const user = userEvent.setup();
    await screen.findByRole("heading", { name: "Customer workspace" });
    expect(screen.getAllByRole("option")).toHaveLength(3);
    // Covers both directions and Customer -> Business as well as the original
    // Customer -> Creator -> Business -> Customer failing sequence.
    let previous = profiles[0];
    for (const index of [1, 2, 0, 2, 1, 0]) {
      const profile = profiles[index];
      await user.selectOptions(screen.getByLabelText("Switch profile"), String(index));
      await screen.findByRole("heading", { name: `${profile.role} workspace` });
      await waitFor(() => expect(window.location.pathname).toBe(roleHome[profile.role]));
      expect(screen.getByLabelText("Active role")).toHaveTextContent(profile.role);
      expect(window.sessionStorage.getItem("weymela.profile-key")).toBe(profileKey(profile));
      const call = fetch.mock.calls.filter(([path]) => path === "/api/session/switch-profile").at(-1)!;
      expect(JSON.parse(String(call[1]?.body))).toEqual({ role: profile.role, subjectId: profile.subjectId, businessId: profile.businessId });
      const headers = call[1]?.headers as Record<string, string>;
      expect(headers["X-Weymela-Request"]).toBe("1");
      expect(headers["X-Weymela-Profile"]).toBe(profileKey(previous));
      previous = profile;
      expect(screen.getAllByRole("option")).toHaveLength(3);
    }
    expect(commits.length).toBeGreaterThan(1);
    for (const context of commits) {
      expect(context.role).toBeDefined();
      expect(context.path).toBe(roleHome[context.role!]);
    }
    expect(screen.queryByRole("heading", { name: "Workspace unavailable" })).not.toBeInTheDocument();
  });

  it("still rejects manually opening a non-active role route even when that role is approved", async () => {
    const fetch = sessionServer();
    renderSessionRoutes();
    await screen.findByRole("heading", { name: "Customer workspace" });
    await userEvent.click(screen.getByRole("link", { name: "Open Creator directly" }));
    await screen.findByRole("heading", { name: "Workspace unavailable" });
    expect(window.location.pathname).toBe("/unauthorized");
    expect(screen.getByLabelText("Active role")).toHaveTextContent("Customer");
    expect(fetch.mock.calls.filter(([path]) => path === "/api/session/switch-profile")).toHaveLength(0);
  });

  it.each([403, 409, 503])("keeps the active role, route and context when the server rejects switching with %s", async status => {
    sessionServer(status);
    const commits = renderSessionRoutes();
    await screen.findByRole("heading", { name: "Customer workspace" });
    await userEvent.selectOptions(screen.getByLabelText("Switch profile"), "1");
    expect(await screen.findByRole("alert")).toHaveTextContent("That profile is no longer available");
    expect(screen.getByLabelText("Active role")).toHaveTextContent("Customer");
    expect(window.sessionStorage.getItem("weymela.profile-key")).toBe(profileKey(profiles[0]));
    expect(window.location.pathname).toBe("/customer/offers");
    expect(commits.every(context => context.role === "Customer" && context.path === "/customer/offers")).toBe(true);
  });

  it("does not adopt a tampered or unapproved subject when the server rejects it", async () => {
    const fetch = sessionServer();
    const commits = renderSessionRoutes();
    await screen.findByRole("heading", { name: "Customer workspace" });
    await userEvent.click(screen.getByRole("button", { name: "Attempt tampered profile" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Unapproved switch rejected");
    const call = fetch.mock.calls.find(([path]) => path === "/api/session/switch-profile")!;
    expect(JSON.parse(String(call[1]?.body))).toEqual({ role: "Creator", subjectId: "unapproved-creator", businessId: null });
    expect(window.sessionStorage.getItem("weymela.profile-key")).toBe(profileKey(profiles[0]));
    expect(commits.every(context => context.role === "Customer" && context.path === "/customer/offers")).toBe(true);
  });
});
