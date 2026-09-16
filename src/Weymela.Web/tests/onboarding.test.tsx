import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const mocks = vi.hoisted(() => ({ post: vi.fn().mockResolvedValue({ id: "enrollment-1" }), reload: vi.fn(), statusProfiles: [] as Array<{ id: string; role: string | number; status: string | number; displayName: string; publicId: string; submittedAtUtc: string; decisionReason: string | null }>, activeProfiles: [] as Array<{ role: string }> }));
vi.mock("../src/api/client", () => ({
  post: mocks.post,
  useAction: () => ({ busy: false, error: null, run: async (fn: (key: string) => Promise<void>) => fn("enroll-key") }),
  useResource: (path: string) => ({ data: path === "/onboarding/legal" ? {
    current: false,
    documents: [
      { documentId: "terms-id", kind: "TermsOfService", title: "Terms of Service", version: "pilot-1", contentHash: "terms-hash", effectiveFromUtc: "2026-09-01T00:00:00Z", viewPath: "/legal/terms-of-service", accepted: false },
      { documentId: "privacy-id", kind: "PrivacyPolicy", title: "Privacy Policy", version: "pilot-1", contentHash: "privacy-hash", effectiveFromUtc: "2026-09-01T00:00:00Z", viewPath: "/legal/privacy-policy", accepted: false },
    ]
  } : { profiles: mocks.statusProfiles }, loading: false, error: null, reload: mocks.reload }),
}));
vi.mock("../src/app/Session", () => ({
  useSession: () => ({ user: { role: "Onboarding", displayName: "Account setup", publicId: "", profiles: mocks.activeProfiles }, refresh: vi.fn(), signOut: vi.fn() }),
}));

import { Onboarding } from "../src/app/Onboarding";

describe("additional profile onboarding", () => {
  it("shows only the three public choices with concise, accessible wording", () => {
    render(<Onboarding />);
    expect(screen.getByRole("heading", { name: "How do you want to use Weymela?" })).toBeVisible();
    for (const action of ["Use as Customer", "Become a Creator", "Add a Business"])
      expect(screen.getByRole("button", { name: new RegExp(action) })).toBeVisible();
    expect(screen.queryByText(/Platform Admin|Staff|Cashier|Public ID/)).not.toBeInTheDocument();
  });

  it("keeps pending Creator request visible but not selectable and hides identifiers", () => {
    mocks.statusProfiles = [{ id: "request-1", role: 2, status: 0, displayName: "Bella", publicId: "CR-INTERNAL", submittedAtUtc: "2026-09-01T00:00:00Z", decisionReason: null }];
    try {
      render(<Onboarding />);
      expect(screen.getByText("Bella — Pending")).toBeVisible();
      expect(screen.getByRole("button", { name: /Creator.*Pending/ })).toBeDisabled();
      expect(screen.queryByText("CR-INTERNAL")).not.toBeInTheDocument();
      expect(screen.getByRole("button", { name: /Add a Business/ })).toBeEnabled();
    } finally { mocks.statusProfiles = []; }
  });

  it("does not present an approved but inactive profile as switchable or create a second login", () => {
    mocks.statusProfiles = [{ id: "request-2", role: 1, status: 1, displayName: "ABC Café", publicId: "BUS-INTERNAL", submittedAtUtc: "2026-09-01T00:00:00Z", decisionReason: null }];
    try {
      render(<Onboarding />);
      expect(screen.getByText("ABC Café — Unavailable")).toBeVisible();
      expect(screen.getByRole("button", { name: /Business.*Unavailable/ })).toBeDisabled();
      expect(screen.queryByText("BUS-INTERNAL")).not.toBeInTheDocument();
    } finally { mocks.statusProfiles = []; }
  });

  it("offers later public profiles without a second login when Customer is active", () => {
    mocks.activeProfiles = [{ role: "Customer" }];
    try {
      render(<Onboarding />);
      expect(screen.queryByRole("button", { name: /Use as Customer/ })).not.toBeInTheDocument();
      expect(screen.getByRole("button", { name: /Become a Creator/ })).toBeVisible();
      expect(screen.getByRole("button", { name: /Add a Business/ })).toBeVisible();
    } finally { mocks.activeProfiles = []; }
  });

  it("offers only public roles and submits a Creator request without inventing a membership", async () => {
    render(<Onboarding />);
    expect(screen.getByRole("button", { name: /Become a Creator/ })).toBeVisible();
    expect(screen.getByRole("button", { name: /Add a Business/ })).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: /Become a Creator/ }));
    await userEvent.type(screen.getByLabelText("Display name"), "Bella");
    await userEvent.type(screen.getByLabelText("Public ID"), "CR-1");
    await userEvent.click(screen.getByRole("button", { name: "Submit for review" }));
    expect(mocks.post).toHaveBeenCalledWith("/onboarding/profile", expect.objectContaining({ role: "Creator", displayName: "Bella", publicId: "CR-1" }), "enroll-key");
  });

  it("activates a Customer without a review action", async () => {
    render(<Onboarding />);
    await userEvent.click(screen.getByRole("button", { name: /Use as Customer/ }));
    await userEvent.type(screen.getByLabelText("Display name"), "Hana");
    await userEvent.type(screen.getByLabelText("Public ID"), "CU-1");
    expect(screen.getByRole("button", { name: "Continue" })).toBeVisible();
    expect(screen.queryByRole("button", { name: "Submit for review" })).toBeNull();
    const consent = screen.getByRole("checkbox", { name: /Terms of Service and Privacy Policy/ });
    expect(consent).not.toBeChecked();
    expect(screen.getByRole("link", { name: "Terms of Service" })).toHaveAttribute("href", "/legal/terms-of-service");
    expect(screen.getByRole("link", { name: "Privacy Policy" })).toHaveAttribute("href", "/legal/privacy-policy");
    await userEvent.click(consent);
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));
    expect(mocks.post).toHaveBeenLastCalledWith("/onboarding/profile", expect.objectContaining({
      role: "Customer", displayName: "Hana", publicId: "CU-1",
      accountLegal: {
        termsOfService: { documentId: "terms-id", contentHash: "terms-hash", accepted: true },
        privacyPolicy: { documentId: "privacy-id", contentHash: "privacy-hash", accepted: true }
      }
    }), "enroll-key");
  });
});
