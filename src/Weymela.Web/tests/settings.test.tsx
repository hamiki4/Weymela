import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import type { AccountClosureOverview, SessionUser } from "../src/api/types";

const state = vi.hoisted(() => ({
  user: null as SessionUser | null,
  closure: null as AccountClosureOverview | null,
  post: vi.fn(),
  reload: vi.fn(),
  refresh: vi.fn(async () => undefined),
  signOut: vi.fn(async () => undefined),
  switchProfile: vi.fn(async () => undefined),
}));

vi.mock("../src/api/client", async (importOriginal) => {
  const original = await importOriginal<typeof import("../src/api/client")>();
  return {
    ...original,
    post: state.post,
    useResource: () => ({ data: state.closure, loading: false, error: null, reload: state.reload }),
  };
});
vi.mock("../src/app/Session", async (importOriginal) => {
  const original = await importOriginal<typeof import("../src/app/Session")>();
  return { ...original, useSession: () => ({ user: state.user, refresh: state.refresh, signOut: state.signOut, switchProfile: state.switchProfile }) };
});

import { ContactPage, DeleteAccountPage, HelpPage, SettingsPage } from "../src/app/SettingsPages";

beforeEach(() => {
  state.user = {
    role: "Customer", displayName: "Mimi", publicId: "CU-100", developmentMode: false, canCheckout: false,
    profiles: [], activeProfileKey: "Customer:customer-1:-",
  };
  state.closure = {
    roles: [
      { role: "Customer", subjectId: "customer-1", displayName: "Mimi", status: "Eligible", blockers: [] },
      { role: "Creator", subjectId: "creator-1", displayName: "Mimi Creator", status: "ActionRequired", blockers: ["Receive the remaining Creator earnings before closing this role."] },
    ],
  };
  state.post.mockReset();
  state.reload.mockReset();
  state.refresh.mockClear();
  state.signOut.mockClear();
  state.switchProfile.mockClear();
});

describe("role-aware Settings", () => {
  it("contains Language, Help, Contact, selective deletion and Sign Out", () => {
    render(<MemoryRouter><SettingsPage /></MemoryRouter>);
    expect(screen.getByRole("group", { name: "Language" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Help" })).toHaveAttribute("href", "/settings/help");
    expect(screen.getByRole("link", { name: "Contact Us" })).toHaveAttribute("href", "/settings/contact");
    expect(screen.getByRole("link", { name: /Delete Account/ })).toHaveAttribute("href", "/settings/delete-account");
    expect(screen.getByRole("button", { name: "Sign Out" })).toBeInTheDocument();
  });

  it("shows concise help for the current role", () => {
    render(<MemoryRouter><HelpPage /></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "Customer Help" })).toBeInTheDocument();
    expect(screen.getByText("Discover promotions")).toBeInTheDocument();
    expect(screen.getByText("How cashback works")).toBeInTheDocument();
    expect(screen.queryByText("Approve Creators")).not.toBeInTheDocument();
  });

  it("uses direct support links without a contact form", () => {
    render(<MemoryRouter><ContactPage /></MemoryRouter>);
    expect(screen.getByRole("link", { name: /support@weymela.com/ })).toHaveAttribute("href", "mailto:support@weymela.com");
    expect(screen.getByRole("link", { name: /\+251911111111/ })).toHaveAttribute("href", "tel:+251911111111");
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
  });

  it("requires explicit confirmation and sends only the selected role", async () => {
    state.post.mockResolvedValue({ role: "Creator", subjectId: "creator-1", status: "Pending",
      blockers: ["Receive the remaining Creator earnings before closing this role."], remainingRoles: 2, nextRole: null });
    render(<MemoryRouter><DeleteAccountPage /></MemoryRouter>);
    await userEvent.click(screen.getByRole("radio", { name: /Creator/ }));
    await userEvent.click(screen.getByRole("button", { name: "Request Closure" }));
    const dialog = within(screen.getByRole("dialog", { name: "Confirm account closure" }));
    expect(dialog.getByRole("button", { name: "Confirm" })).toBeDisabled();
    await userEvent.click(dialog.getByRole("checkbox", { name: /I understand/ }));
    await userEvent.click(dialog.getByRole("button", { name: "Confirm" }));
    expect(state.post).toHaveBeenCalledWith("/account/closure", {
      role: "Creator", subjectId: "creator-1", confirmation: "DELETE",
    }, expect.any(String));
    expect(await screen.findByText(/Closure is pending/)).toBeVisible();
  });

  it("does not show a role selector for a single-role account", () => {
    state.closure = { roles: [state.closure!.roles[0]] };
    render(<MemoryRouter><DeleteAccountPage /></MemoryRouter>);
    expect(screen.queryByRole("radio")).not.toBeInTheDocument();
    expect(screen.getByText("Mimi")).toBeInTheDocument();
  });
});
