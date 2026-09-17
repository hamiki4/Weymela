import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
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
  handoff: vi.fn().mockResolvedValue(undefined),
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
vi.mock("../src/app/ProductIntegration", () => ({
  beginProductHandoff: mocks.handoff,
  useProductIntegrationConfiguration: () => ({
    data: { enabled: true, beginUrl: "https://product.test/begin", callbackId: "test" },
    loading: false,
    error: null,
    reload: vi.fn(),
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
    mocks.handoff.mockClear();
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
        role: 2,
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
    expect(screen.getByRole("button", { name: /Creator.*Not approved.*contact support/ })).toBeDisabled();
    expect(screen.queryByRole("button", { name: /Become a Creator/ })).not.toBeInTheDocument();
  });

  it("keeps one identity and opens an already-added Customer through authoritative profile selection", async () => {
    const profile = { role: "Customer" };
    mocks.activeProfiles = [profile];
    renderOnboarding();
    await userEvent.click(screen.getByRole("button", { name: /Customer.*Already added/ }));
    expect(mocks.switchProfile).toHaveBeenCalledWith(profile);
    expect(screen.getByRole("button", { name: /Become a Creator/ })).toBeEnabled();
    expect(screen.getByRole("button", { name: /Add a Business/ })).toBeEnabled();
  });

  it("uses a purple Creator handoff shell without exposing a replacement product form", async () => {
    renderOnboarding();
    await userEvent.click(screen.getByRole("button", { name: /Become a Creator/ }));
    const shell = screen.getByRole("heading", { name: "Creator setup" }).closest("section");
    expect(shell).toHaveClass("role-creator");
    expect(shell).toHaveAttribute("data-role-theme", "creator");
    expect(within(shell!).getByText(/existing Weymela profile setup/)).toBeVisible();
    expect(within(shell!).queryByRole("textbox")).not.toBeInTheDocument();
    expect(screen.queryByText("Public ID")).not.toBeInTheDocument();
  });

  it("uses a blue Business handoff shell without exposing the old generic form", async () => {
    renderOnboarding();
    await userEvent.click(screen.getByRole("button", { name: /Add a Business/ }));
    const shell = screen.getByRole("heading", { name: "Business setup" }).closest("section");
    expect(shell).toHaveClass("role-business");
    expect(shell).toHaveAttribute("data-role-theme", "business");
    expect(within(shell!).getByText(/existing Weymela profile setup/)).toBeVisible();
    expect(within(shell!).queryByRole("textbox")).not.toBeInTheDocument();
  });

  it("accepts current legal versions then starts Customer product onboarding without collecting identity data", async () => {
    renderOnboarding();
    await userEvent.click(screen.getByRole("button", { name: /Use as Customer/ }));
    const shell = screen.getByRole("heading", { name: "Use as Customer" }).closest("section");
    expect(shell).toHaveClass("role-customer");
    expect(shell).toHaveAttribute("data-role-theme", "customer");
    expect(screen.queryByLabelText(/Preferred name|Email|Phone|Password|PIN|Public ID/i)).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Terms of Service" })).toHaveAttribute(
      "href",
      "/legal/terms-of-service",
    );
    expect(screen.getByRole("link", { name: "Privacy Policy" })).toHaveAttribute(
      "href",
      "/legal/privacy-policy",
    );
    expect(screen.getByText(/I agree to the/)).toBeVisible();
    expect(screen.getByText(/and acknowledge the/)).toBeVisible();
    expect(screen.queryByText(/Review these documents/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Version pilot-1/)).not.toBeInTheDocument();

    await userEvent.click(
      screen.getByRole("checkbox", {
        name: /I agree to the Terms of Service and acknowledge the Privacy Policy/,
      }),
    );
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));

    expect(mocks.post).toHaveBeenCalledWith(
      "/integration/product/legal-acceptance",
      { confirmation: {
          termsOfService: {
            documentId: "terms-id",
            contentHash: "terms-hash",
            accepted: true,
          },
          privacyPolicy: {
            documentId: "privacy-id",
            contentHash: "privacy-hash",
            accepted: true,
          },
        } },
    );
    expect(mocks.handoff).toHaveBeenCalledWith("Customer", "PROFILE_ONBOARDING");
  });

  it("fails closed with a clear Customer state when legal documents are unavailable", async () => {
    mocks.legalStatus = { available: false, current: false, documents: [] };
    renderOnboarding();
    await userEvent.click(screen.getByRole("button", { name: /Use as Customer/ }));
    expect(
      screen.getByRole("heading", { name: "Profile setup isn't available yet." }),
    ).toBeVisible();
    expect(
      screen.getByText("Required terms and privacy information have not been published."),
    ).toBeVisible();
    expect(screen.queryByLabelText("Preferred name")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Continue" })).not.toBeInTheDocument();
    expect(mocks.post).not.toHaveBeenCalled();
  });
});
