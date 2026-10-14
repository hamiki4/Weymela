import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
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
  identityEmail: "test@example.test",
  identityUserId: "isolated-user",
  identityPhone: null as string | null,
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
      path.startsWith("/onboarding/legal") ? mocks.legalStatus
        : path === "/onboarding/identity" ? { userId: mocks.identityUserId, email: mocks.identityEmail,
            phone: mocks.identityPhone, hasProfiles: mocks.activeProfiles.length > 0 || mocks.statusProfiles.length > 0 }
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
  afterEach(() => vi.unstubAllEnvs());
  beforeEach(() => {
    window.sessionStorage.clear();
    mocks.post.mockClear();
    mocks.reload.mockClear();
    mocks.refresh.mockClear();
    mocks.switchProfile.mockClear();
    mocks.statusProfiles = [];
    mocks.activeProfiles = [];
    mocks.legalStatus = effectiveLegal;
    mocks.identityEmail = "test@example.test";
    mocks.identityUserId = "isolated-user";
    mocks.identityPhone = null;
  });
  it.each(["Customer", "Creator", "Business"])("completes owner-deferred %s signup without claiming policy acceptance", async role => {
    vi.stubEnv("VITE_FIREBASE_PROJECT_ID", "weymela-production");
    mocks.legalStatus = { required: false, available: false, current: false, documents: [] };
    window.sessionStorage.setItem("weymela.public-signup", JSON.stringify({ version: 1, idempotencyKey: "deferred-" + role,
      role, legalName: "Isolated signup", email: "test@example.test", phone: "+251900000000",
      businessName: "Isolated business", businessType: "Retail", socialPlatform: "TikTok",
      socialProfileUrl: "https://www.tiktok.com/@isolated_creator", legalAccepted: false, legalDeferred: true }));
    renderOnboarding();
    await waitFor(() => expect(mocks.post).toHaveBeenCalledWith("/onboarding/registration",
      expect.objectContaining({ role }), "deferred-" + role));
    expect(mocks.post.mock.calls[0][1]).not.toHaveProperty("accountLegal");
  });

  it.each(["Business", "Creator", "Customer"])("keeps verified %s registration blocked with an accurate unpublished-policy message", async role => {
    mocks.legalStatus = { available: false, current: false, documents: [] };
    window.sessionStorage.setItem("weymela.public-signup", JSON.stringify({
      version: 1, idempotencyKey: "policy-pending-key", role,
      legalName: "Isolated signup", email: "test@example.test", phone: "+251900000000",
      businessName: "Isolated business", businessType: "Retail", legalAccepted: true,
    }));
    renderOnboarding();
    expect(await screen.findByText("Your email is verified. Account activation requires published Terms of Service and Privacy Policy.")).toBeVisible();
    expect(mocks.post).not.toHaveBeenCalled();
    expect(screen.queryByText("Your Business account is ready.")).not.toBeInTheDocument();
    expect(screen.queryByText("Your account is ready.")).not.toBeInTheDocument();
  });

  it.each(["Business", "Creator", "Customer"])("does not turn an earlier %s checkbox into acceptance of newly published policies", async role => {
    vi.stubEnv("VITE_FIREBASE_PROJECT_ID", "weymela-production");
    window.sessionStorage.setItem("weymela.public-signup", JSON.stringify({
      version: 1, idempotencyKey: "unpublished-checkbox-key", role,
      legalName: "Isolated signup", email: "test@example.test", phone: "+251900000000",
      businessName: "Isolated business", businessType: "Retail", legalAccepted: true,
    }));
    renderOnboarding();
    expect(await screen.findByText("Review and accept the published policies to finish registration.")).toBeVisible();
    expect(mocks.post).not.toHaveBeenCalled();
    expect(screen.getByRole("checkbox")).not.toBeChecked();
    expect(screen.getByRole("button", { name: "Finish registration" })).toBeDisabled();
  });

  it("submits exactly the policy versions explicitly reviewed after Production email verification", async () => {
    vi.stubEnv("VITE_FIREBASE_PROJECT_ID", "weymela-production");
    window.sessionStorage.setItem("weymela.public-signup", JSON.stringify({
      version: 1, idempotencyKey: "explicit-current-consent", role: "Customer",
      legalName: "Isolated signup", email: "test@example.test", phone: "+251900000000", legalAccepted: true,
    }));
    renderOnboarding();
    expect(await screen.findByRole("checkbox")).not.toBeChecked();
    expect(screen.getByRole("link", { name: "Terms of Service" })).toHaveAttribute("href", "/legal/terms-of-service");
    expect(screen.getByRole("link", { name: "Privacy Policy" })).toHaveAttribute("href", "/legal/privacy-policy");
    await userEvent.click(screen.getByRole("checkbox"));
    expect(mocks.post).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole("button", { name: "Finish registration" }));
    await waitFor(() => expect(mocks.post).toHaveBeenCalledTimes(1));
    expect(mocks.post).toHaveBeenCalledWith("/onboarding/registration", expect.objectContaining({
      accountLegal: {
        termsOfService: { documentId: "terms-id", contentHash: "terms-hash", accepted: true },
        privacyPolicy: { documentId: "privacy-id", contentHash: "privacy-hash", accepted: true },
      },
    }), "explicit-current-consent");
    expect(await screen.findByRole("heading", { name: "Your account is ready." })).toBeVisible();
  });

  it("reloads unpublished policies on retry instead of repeatedly submitting stale status", async () => {
    mocks.legalStatus = { available: false, current: false, documents: [] };
    window.sessionStorage.setItem("weymela.public-signup", JSON.stringify({
      version: 1, idempotencyKey: "policy-retry", role: "Customer",
      legalName: "Isolated signup", email: "test@example.test", phone: "+251900000000", legalAccepted: true,
    }));
    renderOnboarding();
    await userEvent.click(await screen.findByRole("button", { name: "Try again" }));
    expect(mocks.reload).toHaveBeenCalledTimes(1);
    expect(mocks.post).not.toHaveBeenCalled();
  });

  it("clears explicit confirmation when the published versions change while the form is open", async () => {
    vi.stubEnv("VITE_FIREBASE_PROJECT_ID", "weymela-production");
    window.sessionStorage.setItem("weymela.public-signup", JSON.stringify({
      version: 1, idempotencyKey: "changed-policy-key", role: "Customer",
      legalName: "Isolated signup", email: "test@example.test", phone: "+251900000000", legalAccepted: true,
    }));
    const view = renderOnboarding();
    await userEvent.click(await screen.findByRole("checkbox"));
    expect(screen.getByRole("button", { name: "Finish registration" })).toBeEnabled();
    mocks.legalStatus = { ...effectiveLegal, documents: effectiveLegal.documents.map(document => ({
      ...document, documentId: document.documentId + "-new", contentHash: document.contentHash + "-new",
    })) };
    view.rerender(<MemoryRouter initialEntries={["/onboarding"]}><Onboarding /></MemoryRouter>);
    expect(screen.getByRole("checkbox")).not.toBeChecked();
    expect(screen.getByRole("button", { name: "Finish registration" })).toBeDisabled();
    expect(mocks.post).not.toHaveBeenCalled();
  });

  it("completes a PIN-enrolled public Business registration exactly once and opens its workspace", async () => {
    mocks.identityEmail = "owner@example.test";
    mocks.legalStatus = { ...effectiveLegal, current: true };
    window.sessionStorage.setItem("weymela.public-signup", JSON.stringify({
      version: 1, idempotencyKey: "public-business-key", role: "Business",
      legalName: "Abebe Kebede", email: "owner@example.test", phone: "+251911111111",
      businessName: "ABC Trading", businessType: "Retail", legalAccepted: true,
    }));
    renderOnboarding();
    await waitFor(() => expect(mocks.post).toHaveBeenCalledTimes(1));
    expect(mocks.post).toHaveBeenCalledWith("/onboarding/registration", expect.objectContaining({
      role: "Business", displayName: "ABC Trading", legalName: "Abebe Kebede",
      registeredPhone: "+251911111111", category: "Retail",
    }), "public-business-key");
    expect(await screen.findByRole("heading", { name: "Your Business account is ready." })).toBeVisible();
    expect(screen.getByText(/secure device is registered/)).toBeVisible();
    expect(screen.getByRole("button", { name: "Continue" })).toBeVisible();
    expect(screen.queryByText(/waiting for Weymela approval/)).not.toBeInTheDocument();
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
    expect(screen.getByRole("button", { name: /Customer.*Already added/ })).toBeEnabled();
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
    await waitFor(() => expect(mocks.post).toHaveBeenCalledWith("/onboarding/registration", expect.objectContaining({
      role: "Creator", displayName: "Bella Creates", region: null,
      socialProfiles: [{ platform: "TikTok", profileUrl: "https://www.tiktok.com/@bella", audienceCount: 0 }],
    }), expect.any(String)));
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
    await userEvent.click(screen.getByRole("button", { name: "Add Business" }));
    await waitFor(() => expect(mocks.post).toHaveBeenCalledWith("/onboarding/registration", expect.objectContaining({
      role: "Business", displayName: "Bella Restaurant", category: null, submission: null,
    }), expect.any(String)));
    expect(mocks.refresh).toHaveBeenCalled();
    expect(screen.queryByText("Your Business profile is waiting for approval.")).not.toBeInTheDocument();
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
  it("Business Add Customer ignores an unrelated saved signup phone and reuses the authenticated identity", async () => {
    mocks.activeProfiles = [{ role: "Business" }];
    mocks.identityPhone = "+14045550123";
    mocks.legalStatus = { ...effectiveLegal, current: true };
    sessionStorage.setItem("weymela.public-signup", JSON.stringify({ version: 1, idempotencyKey: "old-signup",
      role: "Customer", legalName: "Old details", email: "other@example.test", phone: "+254712345678", legalAccepted: true }));
    renderOnboarding();
    expect(mocks.post).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole("button", { name: /Use as Customer/ }));
    await userEvent.type(screen.getByLabelText("Preferred name"), "Same account customer");
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));
    await waitFor(() => expect(mocks.post).toHaveBeenCalledTimes(1));
    expect(mocks.post.mock.calls[0][0]).toBe("/onboarding/profiles");
    expect(mocks.post.mock.calls[0][1]).toEqual({ role: "Customer", displayName: "Same account customer" });
    expect(mocks.post.mock.calls.some(call => String(call[0]).includes("password") || String(call[0]).includes("registration-phone"))).toBe(false);
  });

  it("does not submit another identity's saved signup and offers recovery under the current identity", async () => {
    mocks.legalStatus = { required: false, available: false, current: false, documents: [] };
    sessionStorage.setItem("weymela.public-signup", JSON.stringify({ version: 1, idempotencyKey: "wrong-identity",
      role: "Customer", legalName: "Other account", email: "other@example.test", phone: "+254712345678", legalDeferred: true }));
    renderOnboarding();
    expect(await screen.findByText(/These saved details belong to another email/)).toBeVisible();
    expect(mocks.post).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole("button", { name: "Edit profile details" }));
    expect(screen.getByRole("heading", { name: "How do you want to use Weymela?" })).toBeVisible();
    expect(sessionStorage.getItem("weymela.public-signup")).toContain("wrong-identity");
  });

  it("preserves profile setup across refresh and isolates it by account", async () => {
    mocks.activeProfiles = [{ role: "Business" }];
    mocks.legalStatus = { ...effectiveLegal, current: true };
    const view = renderOnboarding();
    await userEvent.click(screen.getByRole("button", { name: /Use as Customer/ }));
    await userEvent.type(screen.getByLabelText("Preferred name"), "Saved customer");
    const saved = JSON.parse(sessionStorage.getItem("weymela.profile-setup.isolated-user")!);
    view.unmount();
    const refreshed = renderOnboarding();
    expect(screen.getByLabelText("Preferred name")).toHaveValue("Saved customer");
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));
    await waitFor(() => expect(mocks.post).toHaveBeenCalledWith("/onboarding/profiles",
      { role: "Customer", displayName: "Saved customer" }, saved.idempotencyKey));
    refreshed.unmount();
    mocks.identityUserId = "another-user";
    renderOnboarding();
    expect(screen.queryByLabelText("Preferred name")).not.toBeInTheDocument();
  });

  it("opens an already-owned profile without creating another record", async () => {
    mocks.activeProfiles = [{ role: "Customer" }];
    renderOnboarding();
    await userEvent.click(screen.getByRole("button", { name: /Customer.*Already added/ }));
    expect(mocks.switchProfile).toHaveBeenCalledWith(mocks.activeProfiles[0]);
    expect(mocks.post).not.toHaveBeenCalled();
  });

});
