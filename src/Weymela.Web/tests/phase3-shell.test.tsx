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
    expect(screen.getByRole("button", { name: "Open Settings" })).toHaveAttribute("aria-controls", "settings-menu");
    expect(screen.getByRole("button", { name: "Open Settings" }).querySelector("svg circle[cx='12'][cy='12']")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Your notifications" }).querySelector("svg[aria-hidden='true']")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Open account menu" })).not.toBeInTheDocument();
    expect(document.querySelector(".topbar-right .small-avatar")).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Open Settings" }));
    const sheet = within(screen.getByRole("dialog", { name: "Settings" }));
    expect(sheet.getByLabelText("Switch profile")).toBeInTheDocument();
    expect(sheet.getByRole("button", { name: "Notifications" })).toBeInTheDocument();
    expect(sheet.getByRole("button", { name: "Location" })).toBeInTheDocument();
    expect(sheet.getByRole("button", { name: "Sign Out" })).toBeInTheDocument();
    for (const label of extra) expect(sheet.getByRole("link", { name: label })).toBeInTheDocument();
    if (role === "Customer") expect(sheet.queryByRole("link", { name: "Social Profiles" })).not.toBeInTheDocument();
    if (role === "Creator") expect(sheet.getByRole("link", { name: "Social Profiles" })).toHaveAttribute("href", "/profile#social-profiles");
    if (role === "Business") {
      expect(sheet.getByRole("link", { name: "Cashier Management" })).toHaveAttribute("href", "/business/cashiers");
      expect(sheet.queryByRole("link", { name: "Create Cashier" })).not.toBeInTheDocument();
      expect(sheet.getByRole("link", { name: "Add Profile" })).toHaveAttribute("href", "/onboarding");
    }
    expect(sheet.queryByText("INTERNAL-ID")).not.toBeInTheDocument();
    await userEvent.selectOptions(sheet.getByLabelText("Switch profile"), "1");
    expect(state.switchProfile).toHaveBeenCalledWith(state.user?.profiles?.[1]);
  });

  it("reports browser notification and location permission results from device APIs", async () => {
    const permission = vi.fn(async () => "granted" as NotificationPermission);
    vi.stubGlobal("Notification", { requestPermission: permission });
    const geolocation = Object.getOwnPropertyDescriptor(navigator, "geolocation");
    const locate = vi.fn((success: PositionCallback) => success({ timestamp: 0 } as GeolocationPosition));
    Object.defineProperty(navigator, "geolocation", { configurable: true, value: { getCurrentPosition: locate } });
    try {
      setup("Customer", "/customer/offers");
      await userEvent.click(screen.getByRole("button", { name: "Open Settings" }));
      const sheet = within(screen.getByRole("dialog", { name: "Settings" }));
      await userEvent.click(sheet.getByRole("button", { name: "Notifications" }));
      expect(permission).toHaveBeenCalledOnce();
      expect(await sheet.findByRole("status")).toHaveTextContent("granted");
      await userEvent.click(sheet.getByRole("button", { name: "Location" }));
      expect(locate).toHaveBeenCalledOnce();
      expect(sheet.getByRole("status")).toHaveTextContent("Location access granted for this request.");
    } finally {
      if (geolocation) Object.defineProperty(navigator, "geolocation", geolocation);
      else Reflect.deleteProperty(navigator, "geolocation");
    }
  });

  it("keeps internal Admin navigation separate from public add-profile choice", () => {
    setup("PlatformAdmin", "/admin");
    expect(document.querySelector(".workspace-label")).toHaveTextContent("Platform Admin / Weymela");
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
