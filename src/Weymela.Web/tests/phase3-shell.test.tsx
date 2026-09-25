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

afterEach(() => { state.user = null; vi.restoreAllMocks(); });

describe("Phase 3 multi-profile shell", () => {
  it.each([
    ["Customer", "/customer/offers", "role-customer"],
    ["Creator", "/creator", "role-creator"],
    ["Business", "/business", "role-business"],
  ] as const)("uses the %s accent and hides public/internal identifiers", (role, path, theme) => {
    setup(role, path);
    expect(document.querySelector(".app-shell")).toHaveClass(theme);
    expect(document.querySelector(".workspace-label")).toHaveTextContent(`${role} / Weymela`);
    expect(screen.queryByText("INTERNAL-ID")).not.toBeInTheDocument();
    expect(screen.queryByText("subject-secret")).not.toBeInTheDocument();
    if (role === "Business") {
      expect(within(screen.getByRole("navigation", { name: "Mobile navigation" }))
        .getByRole("button", { name: "Profile" })).toHaveAttribute("aria-controls", "account-menu");
    } else {
      const addProfileLinks = screen.getAllByRole("link", { name: "Add a profile", hidden: true });
      expect(addProfileLinks).toHaveLength(role === "Creator" ? 1 : 2);
      expect(addProfileLinks.every((link) => link.getAttribute("href") === "/onboarding")).toBe(true);
    }
    const mobile = screen.getByRole("navigation", { name: "Mobile navigation" });
    if (role === "Customer") {
      expect(within(mobile).getByRole("link", { name: "Home" })).toBeInTheDocument();
      expect(within(mobile).getByRole("link", { name: "Discover" })).toBeInTheDocument();
      expect(within(mobile).getByRole("link", { name: "Transactions" })).toHaveAttribute("href", "/customer/transactions");
      expect(within(mobile).getByRole("link", { name: "Cashback" })).toBeInTheDocument();
      expect(within(mobile).getAllByRole("link")).toHaveLength(4);
      expect(within(mobile).getByRole("button", { name: "Profile" })).toHaveAttribute("aria-controls", "account-menu");
      expect(within(mobile).queryByRole("button", { name: "More navigation" })).not.toBeInTheDocument();
    } else if (role === "Creator") {
      expect(within(mobile).getAllByRole("link").map((link) => link.textContent)).toEqual([
        "Home", "Discover", "My Promotions", "Earnings",
      ]);
      expect(within(mobile).getByRole("button", { name: "Profile" })).toHaveAttribute("aria-controls", "account-menu");
      expect(within(mobile).getAllByRole("link")).toHaveLength(4);
      expect(within(mobile).queryByRole("button", { name: "More navigation" })).not.toBeInTheDocument();
    } else {
      expect(within(mobile).getAllByRole("link").map((link) => link.textContent)).toEqual([
        "Home", "Promotions", "UGC", "Checkout",
      ]);
      expect(within(mobile).getByRole("button", { name: "Profile" })).toHaveAttribute("aria-controls", "account-menu");
      expect(within(mobile).getAllByRole("link")).toHaveLength(4);
    }
  });

  it("opens the account menu without exposing identifiers and preserves profile switching", async () => {
    setup("Customer", "/customer/offers", true);
    expect(screen.getAllByLabelText("Switch profile")).toHaveLength(2);
    expect(screen.getAllByLabelText("Switch profile")[0]).toBeEnabled();
    expect(screen.getAllByLabelText("Switch profile")[1]).toBeEnabled();
    const ids = screen.getAllByLabelText("Switch profile").map((element) => element.id);
    expect(new Set(ids).size).toBe(2);
    expect(document.querySelector(".profile-switcher option")?.getAttribute("value")).toBe("0");
    const mobile = screen.getByRole("navigation", { name: "Mobile navigation" });
    expect(within(mobile).getByRole("link", { name: "Home" })).toHaveAttribute("href", "/customer/offers");
    expect(within(mobile).getByRole("link", { name: "Discover" })).toHaveAttribute("href", "/customer/discover");
    expect(within(mobile).getByRole("link", { name: "Transactions" })).toHaveAttribute("href", "/customer/transactions");
    expect(within(mobile).getByRole("link", { name: "Cashback" })).toHaveAttribute("href", "/customer/cashback");
    expect(within(mobile).getByRole("button", { name: "Profile" })).toHaveAttribute("aria-controls", "account-menu");
    expect(within(mobile).queryByRole("button", { name: "More navigation" })).not.toBeInTheDocument();
    await userEvent.click(within(mobile).getByRole("button", { name: "Profile" }));
    const profileMenu = screen.getByRole("dialog", { name: "Account menu", hidden: true });
    expect(within(profileMenu).getByLabelText("Switch profile")).toBeInTheDocument();
    expect(within(profileMenu).getByRole("link", { name: "Add a profile", hidden: true })).toHaveAttribute("href", "/onboarding");
    const account = screen.getByRole("button", { name: "Open account menu" });
    await userEvent.click(within(profileMenu).getByRole("button", { name: "Close account menu", hidden: true }));
    account.focus();
    await userEvent.keyboard("{Enter}");
    const menu = screen.getByRole("dialog", { name: "Account menu", hidden: true });
    expect(within(menu).getByText("Hana")).toBeInTheDocument();
    expect(within(menu).getByText("Customer")).toBeInTheDocument();
    expect(within(menu).getByLabelText("Switch profile")).toBeInTheDocument();
    expect(within(menu).getByRole("button", { name: "Sign out", hidden: true })).toBeInTheDocument();
    expect(within(menu).getByRole("button", { name: "Close account menu", hidden: true })).toBeInTheDocument();
    expect(within(menu).queryByText("INTERNAL-ID")).not.toBeInTheDocument();
    expect(within(menu).queryByText("PUBLIC-ID")).not.toBeInTheDocument();
    await userEvent.selectOptions(within(menu).getByLabelText("Switch profile"), "1");
    expect(state.switchProfile).toHaveBeenCalledWith(state.user?.profiles?.[1]);
  });

  it("keeps Creator navigation to five destinations and Profile opens the shared account menu", async () => {
    setup("Creator", "/creator");
    const mobile = screen.getByRole("navigation", { name: "Mobile navigation" });
    expect(within(mobile).getAllByRole("link").map((link) => link.textContent)).toEqual([
      "Home", "Discover", "My Promotions", "Earnings",
    ]);
    expect(within(mobile).queryByRole("link", { name: /Find Businesses|Campaign Requests|Payouts|How You Earn|Add a profile/ })).not.toBeInTheDocument();
    await userEvent.click(within(mobile).getByRole("button", { name: "Profile" }));
    const menu = screen.getByRole("dialog", { name: "Account menu" });
    expect(within(menu).getByLabelText("Switch profile")).toBeInTheDocument();
    expect(within(menu).getByRole("link", { name: "Add a profile" })).toHaveAttribute("href", "/onboarding");
    expect(within(menu).getByRole("button", { name: "Sign out" })).toBeInTheDocument();
  });

  it("keeps internal Admin navigation separate from public add-profile choice", () => {
    setup("PlatformAdmin", "/admin");
    expect(document.querySelector(".workspace-label")).toHaveTextContent("Platform Admin / Weymela");
    expect(screen.queryByRole("link", { name: "Add a profile" })).not.toBeInTheDocument();
  });

  it("gives Operations Admin an operational navigation without platform controls", () => {
    setup("OperationsAdmin", "/admin/operations");
    const navigation = screen.getByRole("navigation", { name: "Main navigation" });
    expect(within(navigation).getByRole("link", { name: "Home" })).toHaveAttribute("href", "/admin/operations");
    expect(within(navigation).getByRole("link", { name: "Payouts" })).toHaveAttribute("href", "/admin/payouts");
    expect(within(navigation).queryByRole("link", { name: /Financial Settings|Platform Revenue|Audit|Accounts/ })).not.toBeInTheDocument();
  });
});
