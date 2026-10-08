import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { Role, SessionUser } from "../src/api/types";

const state = vi.hoisted(() => ({ user: null as SessionUser | null, switchProfile: vi.fn(), signOut: vi.fn() }));
vi.mock("../src/app/Session", async (importOriginal) => {
  const original = await importOriginal<typeof import("../src/app/Session")>();
  return { ...original, useSession: () => state };
});
vi.mock("../src/app/ConnectionStatus", () => ({ ConnectionStatus: () => null }));

import { Shell } from "../src/app/Shell";
import { SettingsPage } from "../src/app/SettingsPages";

function setup(role: Role, path: string, withSecondProfile = false) {
  const profile = { role, subjectId: "subject-secret", businessId: null, displayName: "Hana", publicId: "INTERNAL-ID", canCheckout: false };
  const profiles = withSecondProfile
    ? [profile, { ...profile, subjectId: "subject-two", displayName: "Mina", publicId: "PUBLIC-ID" }]
    : [profile];
  state.user = {
    role, displayName: "Hana", publicId: "INTERNAL-ID",
    canCheckout: false, developmentMode: false,
    profiles,
    activeProfileKey: `${role}:subject-secret:-`,
  };
  render(<MemoryRouter initialEntries={[path]}><Routes><Route path="*" element={<Shell />} /></Routes></MemoryRouter>);
}

afterEach(() => { state.user = null; vi.restoreAllMocks(); vi.unstubAllGlobals(); });

describe("Profile and Settings shell", () => {
  it.each([
    ["Customer", "/customer/offers", ["Add Profile"]],
    ["Creator", "/creator", ["Add Profile", "Social Profiles"]],
    ["Business", "/business", ["Add Profile", "Cashier Management"]],
  ] as const)("separates %s Profile navigation from Settings", async (role, path, extra) => {
    setup(role, path, true);
    const mobile = within(screen.getByRole("navigation", { name: "Mobile navigation" }));
    expect(mobile.getByRole("link", { name: "Profile" })).toHaveAttribute("href", "/profile");
    expect(mobile.queryByRole("button", { name: "Profile" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Your notifications" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Settings" })).toHaveAttribute("href", "/settings");
    expect(screen.getByRole("link", { name: "Open Settings" }).querySelector("svg circle[cx='12'][cy='12']")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Your notifications" }).querySelector("svg[aria-hidden='true']")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Open account menu" })).not.toBeInTheDocument();
    expect(document.querySelector(".topbar-right .small-avatar")).not.toBeInTheDocument();
    expect(screen.queryByRole("dialog", { name: "Settings" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Settings" })).toHaveAttribute("href", "/settings");
    expect(screen.queryByText("INTERNAL-ID")).not.toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText("Switch profile"), "1");
    expect(state.switchProfile).toHaveBeenCalledWith(state.user?.profiles?.[1]);
    expect(extra.length).toBeGreaterThan(0);
  });

  it.each([
    ["Customer", []],
    ["Creator", ["Social Profiles"]],
    ["Business", ["Cashier Management"]],
    ["PlatformAdmin", []],
  ] as const)("keeps the %s Settings page focused on account actions", (role, roleLinks) => {
    setup(role, "/settings");
    render(<MemoryRouter><SettingsPage /></MemoryRouter>);
    expect(screen.getAllByText("Language").length).toBeGreaterThan(0);
    expect(screen.getByRole("link", { name: "Help" })).toHaveAttribute("href", "/settings/help");
    expect(screen.getByRole("link", { name: "Contact Us" })).toHaveAttribute("href", "/settings/contact");
    expect(screen.getByRole("link", { name: /Delete Account/ })).toHaveAttribute("href", "/settings/delete-account");
    expect(screen.getByRole("button", { name: "Sign Out" })).toBeInTheDocument();
    for (const label of roleLinks) expect(screen.getByRole("link", { name: label })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Location" })).not.toBeInTheDocument();
  });

  it("keeps internal Admin navigation separate from public add-profile choice", () => {
    setup("PlatformAdmin", "/admin");
    expect(document.querySelector(".workspace-label")).toHaveTextContent("Platform Admin");
    expect(document.querySelector(".topbar-brand .brand-mark")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Add a profile" })).not.toBeInTheDocument();
  });

  it.each([
    ["/admin/customers/new", "Customers", true],
    ["/admin/businesses/new", "Businesses", true],
    ["/admin/wallets/business", "Wallets", false],
    ["/admin/ugc/promotion", "UGC", true],
  ] as const)("marks the Admin section for child route %s", async (path, label, moreActive) => {
    setup("PlatformAdmin", path);
    const mobile = within(screen.getByRole("navigation", { name: "Mobile navigation" }));
    const more = mobile.getByRole("link", { name: "More navigation" });
    expect(more).toHaveAttribute("href", "/admin/more");
    expect(more.classList.contains("active")).toBe(moreActive);
    if (moreActive) {
      expect(screen.queryByRole("dialog", { name: "More navigation" })).not.toBeInTheDocument();
    } else {
      expect(mobile.getByRole("link", { name: label })).toHaveAttribute("aria-current", "page");
    }
  });

  it("gives Operations Admin an operational navigation without platform controls", () => {
    setup("OperationsAdmin", "/admin/operations");
    const navigation = screen.getByRole("navigation", { name: "Main navigation" });
    expect(within(navigation).getByRole("link", { name: "Home" })).toHaveAttribute("href", "/admin/operations");
    expect(within(navigation).getByRole("link", { name: "Payouts" })).toHaveAttribute("href", "/admin/payouts");
    expect(within(navigation).queryByRole("link", { name: /Financial Settings|Platform Revenue|Audit|Accounts/ })).not.toBeInTheDocument();
  });
});
