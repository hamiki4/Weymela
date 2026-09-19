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

function setup(role: Role, path: string) {
  state.user = {
    role, displayName: "Hana", publicId: "INTERNAL-ID",
    canCheckout: false, developmentMode: false,
    profiles: [{ role, subjectId: "subject-secret", businessId: null, displayName: "Hana", publicId: "INTERNAL-ID", canCheckout: false }],
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
      expect(screen.getByRole("link", { name: "Profile" })).toHaveAttribute("href", "/onboarding");
    } else {
      expect(screen.getByRole("link", { name: "Add a profile" })).toHaveAttribute("href", "/onboarding");
    }
    const mobile = screen.getByRole("navigation", { name: "Mobile navigation" });
    expect(within(mobile).getByRole("button", { name: "More navigation and profiles" })).toBeInTheDocument();
  });

  it("shows active profile and makes the mobile menu reachable by keyboard and touch", async () => {
    setup("Customer", "/customer/offers");
    expect(screen.getAllByLabelText("Switch profile")).toHaveLength(2);
    expect(screen.getAllByLabelText("Switch profile")[0]).toBeDisabled();
    const ids = screen.getAllByLabelText("Switch profile").map((element) => element.id);
    expect(new Set(ids).size).toBe(2);
    expect(document.querySelector(".profile-switcher option")?.getAttribute("value")).toBe("0");
    const mobile = screen.getByRole("navigation", { name: "Mobile navigation" });
    expect(within(mobile).getByRole("link", { name: /Offers for you/ })).toHaveAttribute("href", "/customer/offers");
    const more = within(mobile).getByRole("button", { name: "More navigation and profiles" });
    const showModal = vi.spyOn(HTMLDialogElement.prototype, "showModal").mockImplementation(() => {});
    more.focus();
    await userEvent.keyboard("{Enter}");
    expect(showModal).toHaveBeenCalledTimes(1);
    await userEvent.click(more);
    expect(showModal).toHaveBeenCalledTimes(2);
  });

  it("keeps internal Admin navigation separate from public add-profile choice", () => {
    setup("PlatformAdmin", "/admin");
    expect(document.querySelector(".workspace-label")).toHaveTextContent("Platform Admin / Weymela");
    expect(screen.queryByRole("link", { name: "Add a profile" })).not.toBeInTheDocument();
  });
});
