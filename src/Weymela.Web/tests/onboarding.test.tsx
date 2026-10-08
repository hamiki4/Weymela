import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { actorRoleWireValues } from "../src/api/actorRoleContract";
import type { AccountLegalStatus } from "../src/api/types";

const effectiveLegal: AccountLegalStatus = {
  available: true,
  current: false,
  documents: [
    {
      documentId: "terms-id",
      kind: "TermsOfService",
      title: "Terms of Service",
      version: "pilot-1",
      contentHash: "terms-hash",
      effectiveFromUtc: "2026-09-01T00:00:00Z",
      viewPath: "/legal/terms-of-service",
      accepted: false,
    },
    {
      documentId: "privacy-id",
      kind: "PrivacyPolicy",
      title: "Privacy Policy",
      version: "pilot-1",
      contentHash: "privacy-hash",
      effectiveFromUtc: "2026-09-01T00:00:00Z",
      viewPath: "/legal/privacy-policy",
      accepted: false,
    },
  ],
};

const mocks = vi.hoisted(() => ({
  post: vi.fn().mockResolvedValue({ id: "enrollment-1" }),
  reload: vi.fn(),
  refresh: vi.fn().mockResolvedValue(undefined),
  switchProfile: vi.fn().mockResolvedValue(undefined),
  legalStatus: null as AccountLegalStatus | null,
  statusProfiles: [] as Array<{
    id: string;
    role: string | number;
    status: string | number;
    displayName: string;
    publicId: string;
    submittedAtUtc: string;
    decisionReason: string | null;
  }>,
  activeProfiles: [] as Array<{ role: string }>,
}));

vi.mock("../src/api/client", () => ({
  post: mocks.post,
  useAction: () => ({
    busy: false,
    error: null,
    run: async (fn: (key: string) => Promise<void>) => fn("enroll-key"),
  }),
  useResource: (path: string) => ({
    data:
      path === "/onboarding/legal"
        ? mocks.legalStatus
        : { profiles: mocks.statusProfiles },
    loading: false,
    error: null,
    reload: mocks.reload,
  }),
}));
vi.mock("../src/app/Session", () => ({
  useSession: () => ({
    user: {
      role: "Onboarding",
      displayName: "Account setup",
      publicId: "",
      profiles: mocks.activeProfiles,
    },
    loading: false,
    refresh: mocks.refresh,
    switchProfile: mocks.switchProfile,
    signOut: vi.fn(),
  }),
}));

import { Onboarding } from "../src/app/Onboarding";

function renderOnboarding() {
  return render(
    <MemoryRouter initialEntries={["/onboarding"]}>
      <Onboarding />
    </MemoryRouter>,
  );
}

describe("shared role-themed onboarding", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    mocks.post.mockClear();
    mocks.reload.mockClear();
    mocks.refresh.mockClear();
    mocks.switchProfile.mockClear();
    mocks.statusProfiles = [];
    mocks.activeProfiles = [];
    mocks.legalStatus = effectiveLegal;
  });

  it("completes a verified public Business registration exactly once and shows pending status", async () => {
    window.sessionStorage.setItem("weymela.public-signup", JSON.stringify({
      version: 1, idempotencyKey: "public-business-key", role: "Business",
      legalName: "Abebe Kebede", email: "owner@example.test", phone: "+251911111111",
      businessName: "ABC Trading", businessType: "Retail", legalAccepted: true,
    }));
    renderOnboarding();
    await waitFor(() => expect(mocks.post).toHaveBeenCalledTimes(1));
    expect(mocks.post).toHaveBeenCalledWith("/onboarding/profile", expect.objectContaining({
      role: "Business", displayName: "ABC Trading", legalName: "Abebe Kebede",
      registeredPhone: "+251911111111", category: "Retail",
    }), "public-business-key");
    expect(await screen.findByRole("heading", { name: "Your email is verified." })).toBeVisible();
    expect(screen.getByText(/Business account is waiting for Weymela approval/)).toBeVisible();
    expect(window.sessionStorage.getItem("weymela.public-signup")).toBeNull();
  });

  it("shows only the three public choices with concise accessible wording", () => {
    renderOnboarding();
    expect(
      screen.getByRole("heading", { name: "How do you want to use Weymela?" }),
    ).toBeVisible();
    for (const action of ["Use as Customer", "Become a Creator", "Add a Business"])
      expect(screen.getByRole("button", { name: new RegExp(action) })).toBeVisible();
    expect(
      screen.queryByText(/Platform Admin|Staff|Cashier|Public ID/),
    ).not.toBeInTheDocument();
  });

  it("keeps a pending request visible without exposing its identifier", () => {
    mocks.statusProfiles = [
      {
        id: "request-1",
        role: actorRoleWireValues.Creator,
        status: 0,
        displayName: "Bella",
        publicId: "CR-INTERNAL",
        submittedAtUtc: "2026-09-01T00:00:00Z",
        decisionReason: null,
      },
    ];
    renderOnboarding();
    expect(screen.getByRole("button", { name: /Creator.*Under review/ })).toBeDisabled();
    expect(screen.getByText("Your Creator profile is waiting for approval.")).toBeVisible();
    expect(screen.queryByText("CR-INTERNAL")).not.toBeInTheDocument();
  });

  it("shows a rejected profile as an existing lifecycle state instead of another Add action", () => {
    mocks.statusProfiles = [
      {
        id: "request-2",
        role: "Creator",
        status: "Rejected",
        displayName: "Bella",
        publicId: "CR-INTERNAL",
        submittedAtUtc: "2026-09-01T00:00:00Z",
        decisionReason: "Not approved",
      },
    ];
    renderOnboarding();
    expect(screen.getByText(/Bella.*Rejected/)).toBeVisible();
    expect(screen.getByText("Not approved")).toBeVisible();
    expect(screen.getByRole("button", { name: /Become a Creator/ })).toBeEnabled();
  });

  it("shows an already-added Customer without legacy handoff", () => {
    mocks.activeProfiles = [{ role: "Customer" }];
    renderOnboarding();
    expect(screen.getByRole("button", { name: /Customer.*Already added/ })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Become a Creator/ })).toBeEnabled();
    expect(screen.getByRole("button", { name: /Add a Business/ })).toBeEnabled();
  });

  it("uses the same simplified Creator setup when adding a profile to an existing account", async () => {
    mocks.activeProfiles = [{ role: "Customer" }];
    renderOnboarding();
    await userEvent.click(screen.getByRole("button", { name: /Become a Creator/ }));
    expect(screen.getByRole("heading", { name: "Creator setup" })).toBeVisible();
    expect(screen.getByLabelText("Creator name")).toBeVisible();
    expect(screen.getByLabelText("Region (optional)")).toBeVisible();
    expect(screen.queryByLabelText(/Creator category|About your work/)).not.toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Add profile" })).toHaveLength(4);
  });

  it("opens Creator setup, requires one social link, and submits SelfReported profile data", async () => {
    renderOnboarding();
    await userEvent.click(screen.getByRole("button", { name: /Become a Creator/ }));
    expect(screen.getByRole("heading", { name: "Creator setup" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Submit for Review" })).toBeDisabled();
    expect(screen.getByLabelText("Creator name")).toBeVisible();
    expect(screen.getByLabelText("Region (optional)")).toBeVisible();
    expect(screen.queryByLabelText(/Creator category|About your work/)).not.toBeInTheDocument();
    expect(screen.getAllByText("Not added")).toHaveLength(4);
    expect(screen.getAllByRole("button", { name: "Add profile" })).toHaveLength(4);
    expect(screen.getByLabelText("I agree to Weymela's Rules and Regulations")).not.toBeChecked();
    await userEvent.type(screen.getByLabelText("Creator name"), "Bella Creates");
    await userEvent.click(screen.getAllByRole("button", { name: "Add profile" })[0]);
    await userEvent.type(screen.getByLabelText("TikTok profile URL"), "https://www.tiktok.com/@bella");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    await userEvent.click(screen.getByLabelText("I agree to Weymela's Rules and Regulations"));
    await userEvent.click(screen.getByRole("button", { name: "Submit for Review" }));
    await waitFor(() => expect(mocks.post).toHaveBeenCalledWith("/onboarding/profile", expect.objectContaining({
      role: "Creator", displayName: "Bella Creates", region: null,
      socialProfiles: [{ platform: "TikTok", profileUrl: "https://www.tiktok.com/@bella", audienceCount: 0 }],
    }), "enroll-key"));
    expect(mocks.post.mock.calls[0][1]).toMatchObject({ category: null });
    expect(mocks.post.mock.calls[0][1]).not.toHaveProperty("publicId");
    expect(screen.getByText("Your Creator profile is waiting for approval.")).toBeVisible();
    expect(screen.queryByRole("heading", { name: "Creator setup" })).not.toBeInTheDocument();
  });

  it("opens a focused Business form without payment fields", async () => {
    renderOnboarding();
    await userEvent.click(screen.getByRole("button", { name: /Add a Business/ }));
    expect(screen.getByRole("heading", { name: "Business setup" })).toBeVisible();
    await userEvent.type(screen.getByLabelText("Business name"), "Bella Restaurant");
    expect(screen.getByLabelText("Region (optional)")).toBeVisible();
    expect(screen.queryByLabelText(/Business type|About your Business/)).not.toBeInTheDocument();
    expect(screen.queryByText(/receipt|payment reference|funding/i)).not.toBeInTheDocument();
    await userEvent.click(screen.getByLabelText("I agree to Weymela's Rules and Regulations"));
    await userEvent.click(screen.getByRole("button", { name: "Submit for Review" }));
    await waitFor(() => expect(mocks.post).toHaveBeenCalledWith("/onboarding/profile", expect.objectContaining({
      role: "Business", displayName: "Bella Restaurant", category: null, submission: null,
    }), "enroll-key"));
    expect(screen.getByText("Your Business profile is waiting for approval.")).toBeVisible();
  });

  it("uses the same simplified Business setup when adding a profile to an existing account", async () => {
    mocks.activeProfiles = [{ role: "Creator" }];
    renderOnboarding();
    await userEvent.click(screen.getByRole("button", { name: /Add a Business/ }));
    expect(screen.getByRole("heading", { name: "Business setup" })).toBeVisible();
    expect(screen.getByLabelText("Business name")).toBeVisible();
    expect(screen.getByLabelText("Region (optional)")).toBeVisible();
    expect(screen.queryByLabelText(/Business type|About your Business/)).not.toBeInTheDocument();
  });

  it("fails closed with a clear Customer state when legal documents are unavailable", async () => {
    mocks.legalStatus = { available: false, current: false, documents: [] };
    renderOnboarding();
    await userEvent.click(screen.getByRole("button", { name: /Use as Customer/ }));
    expect(
      screen.getByRole("heading", { name: "Customer setup isn't available yet." }),
    ).toBeVisible();
    expect(
      screen.getByText("Required terms and privacy information have not been published."),
    ).toBeVisible();
    expect(screen.queryByLabelText("Preferred name")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Continue" })).not.toBeInTheDocument();
    expect(mocks.post).not.toHaveBeenCalled();
  });
});
