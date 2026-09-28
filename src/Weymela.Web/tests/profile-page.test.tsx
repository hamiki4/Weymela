import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { ProfilePage } from "../src/app/ProfilePage";
import { mockApi } from "./fixtures";

vi.mock("../src/app/Session", () => ({ useSession: () => ({ user: { displayName: "Bella" } }) }));

const base = { displayName: "Bella", publicId: "CR-100", email: "bella@example.com", phone: null,
  status: "Active", businessType: null, region: null };

function mount(role: "Customer" | "Creator" | "Business", extra: Record<string, unknown> = {}) {
  mockApi({ "/profile": { ...base, role, ...extra }, "/creator/social-accounts": [
    { id: "own-social", platform: "TikTok", profileUrl: "https://www.tiktok.com/@bella", verificationStatus: "Verified" },
  ] });
  render(<MemoryRouter><ProfilePage /></MemoryRouter>);
}

beforeEach(() => vi.restoreAllMocks());

describe("full profile page", () => {
  it("shows only available Customer identity fields", async () => {
    mount("Customer", { displayName: "Mimi", publicId: "CU-100", email: null, phone: "+251900000000" });
    expect(await screen.findByText("CU-100")).toBeVisible();
    expect(screen.getByRole("heading", { name: "Profile", level: 1 })).toBeVisible();
    expect(screen.getByText("+251900000000")).toBeVisible();
    expect(screen.queryByText("Email")).not.toBeInTheDocument();
    expect(screen.queryByText("Social Accounts")).not.toBeInTheDocument();
    expect(screen.queryByText("Business Information")).not.toBeInTheDocument();
    expect(screen.queryByText("Not provided")).not.toBeInTheDocument();
  });

  it("shows Creator social profiles from the server with no fake Add or Manage action", async () => {
    mount("Creator");
    const section = await screen.findByRole("heading", { name: "Social Accounts" });
    expect(section).toBeVisible();
    expect(await screen.findByText("@bella")).toBeVisible();
    expect(screen.getAllByText("Not connected")).toHaveLength(3);
    expect(screen.getByText("Verified")).toBeVisible();
    expect(screen.queryByRole("button", { name: /Add|Manage|Connected/ })).not.toBeInTheDocument();
    expect(screen.queryByText("Phone")).not.toBeInTheDocument();
  });

  it("groups only recorded Business information", async () => {
    mount("Business", { displayName: "Abc Coffee", publicId: "BU-100", businessType: "Cafe", region: "Addis Ababa" });
    const business = await screen.findByRole("heading", { name: "Business Information" });
    expect(business).toBeVisible();
    expect(screen.getByText("Cafe")).toBeVisible();
    expect(screen.getByRole("heading", { name: "Contact Information" })).toBeVisible();
    expect(screen.getByRole("heading", { name: "Address" })).toBeVisible();
    expect(within(screen.getByText("Addis Ababa").closest(".profile-info-row") as HTMLElement).getByText("Region")).toBeVisible();
    expect(screen.queryByText("Phone")).not.toBeInTheDocument();
    expect(screen.queryByText("Wallet")).not.toBeInTheDocument();
    expect(screen.queryByText("Social Accounts")).not.toBeInTheDocument();
  });
});
