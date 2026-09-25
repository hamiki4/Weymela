import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ViewAsSession } from "../src/api/types";

const session = vi.hoisted(() => ({
  startViewAs: vi.fn(),
  endViewAs: vi.fn(),
  viewAs: null as { session: ViewAsSession; displayName: string; returnTo: string } | null,
  viewAsNotice: null as string | null,
}));

vi.mock("../src/app/Session", async (importOriginal) => {
  const original = await importOriginal<typeof import("../src/app/Session")>();
  return { ...original, useSession: () => session };
});

import { ViewAsBanner, ViewAsEntry } from "../src/app/ViewAs";

const viewSession: ViewAsSession = {
  supportSessionId: "support-session",
  viewedUserId: "viewed-user",
  viewedRole: "Business",
  viewedBusinessId: "business-subject",
  viewedCreatorId: null,
  viewedCustomerId: null,
  createdAtUtc: "2026-09-25T00:00:00Z",
  expiresAtUtc: "2026-09-25T00:15:00Z",
};

beforeEach(() => {
  session.startViewAs.mockReset();
  session.endViewAs.mockReset();
  session.viewAs = null;
  session.viewAsNotice = null;
});

describe("B5 View As entry", () => {
  it.each([
    ["Customer", "Mimi Customer"],
    ["Creator", "Cora Creator"],
    ["Business", "Bella Restaurant"],
    ["OperationsAdmin", "Owen Operations"],
  ])("confirms before opening a %s workspace", async (role, name) => {
    session.startViewAs.mockResolvedValue(viewSession);
    render(<MemoryRouter><ViewAsEntry viewedUserId="target-user" displayName={name} role={role} returnTo="/admin/accounts/target-user?mode=view" /></MemoryRouter>);

    await userEvent.click(screen.getByRole("button", { name: "View As" }));
    const dialog = screen.getByRole("dialog", { name: `View as ${name}?` });
    expect(within(dialog).getByText(new RegExp(`read-only Admin View of this ${role === "OperationsAdmin" ? "Operations Admin" : role}`))).toBeVisible();
    await userEvent.click(within(dialog).getByRole("button", { name: "View As" }));
    expect(session.startViewAs).toHaveBeenCalledWith("target-user", name, "/admin/accounts/target-user?mode=view");
  });

  it.each(["PlatformAdmin", "Cashier", "Unsupported"])("does not render an entry for %s", (role) => {
    render(<MemoryRouter><ViewAsEntry viewedUserId="target-user" displayName="Blocked" role={role} returnTo="/admin/accounts/target-user?mode=view" /></MemoryRouter>);
    expect(screen.queryByRole("button", { name: "View As" })).not.toBeInTheDocument();
  });
});

describe("B5 View As banner", () => {
  it("makes the viewed state and exit action unmistakable", async () => {
    session.viewAs = { session: viewSession, displayName: "Bella Restaurant", returnTo: "/admin/accounts/viewed-user?mode=view" };
    render(<MemoryRouter><ViewAsBanner /></MemoryRouter>);
    expect(screen.getByRole("complementary", { name: "Admin View Mode" })).toHaveTextContent("ADMIN VIEW MODE");
    expect(screen.getByText("Viewing: Bella Restaurant")).toBeVisible();
    expect(screen.getByText("Role: Business")).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: "Exit View Mode" }));
    expect(session.endViewAs).toHaveBeenCalledTimes(1);
  });

  it("shows an expiration message after the server invalidates the session", () => {
    session.viewAsNotice = "Admin View Mode expired. You are back in the Platform Admin workspace.";
    render(<MemoryRouter><ViewAsBanner /></MemoryRouter>);
    expect(screen.getByRole("alert")).toHaveTextContent(/Admin View Mode expired/);
  });
});
