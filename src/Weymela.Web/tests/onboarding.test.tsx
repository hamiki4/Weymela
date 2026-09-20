import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
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
    mocks.post.mockClear();
    mocks.reload.mockClear();
    mocks.refresh.mockClear();
    mocks.switchProfile.mockClear();
    mocks.statusProfiles = [];
    mocks.activeProfiles = [];
    mocks.legalStatus = effectiveLegal;
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
    expect(screen.getByRole("button", { name: /Creator.*Pending/ })).toBeDisabled();
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
