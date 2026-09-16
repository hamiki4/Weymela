import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";

const mocks = vi.hoisted(() => ({
  enrollPassword: vi.fn(),
  refresh: vi.fn(),
  phoneEnrolled: false,
  loading: false,
  loadFailed: false,
  userPresent: true,
  securityPresent: true,
}));

vi.mock("../src/app/Session", () => ({
  roleHome: { Customer: "/customer/offers" },
  useSession: () => ({
    loading: mocks.loading,
    loadFailed: mocks.loadFailed,
    user: mocks.userPresent ? { role: "Customer" } : null,
    accountSecurity: mocks.securityPresent ? { passwordEnrolled: false, phoneEnrolled: mocks.phoneEnrolled } : null,
    deviceEnrollment: { state: "EnrollmentRequired" },
    enrollPassword: mocks.enrollPassword,
    refresh: mocks.refresh,
  }),
}));

import { SecuritySetup } from "../src/app/SecuritySetup";

function renderSetup() {
  return render(<MemoryRouter initialEntries={["/security-setup"]}><Routes>
    <Route path="/security-setup" element={<SecuritySetup />} />
    <Route path="/pin-setup" element={<h1>PIN setup</h1>} />
  </Routes></MemoryRouter>);
}

beforeEach(() => {
  mocks.phoneEnrolled = false;
  mocks.loading = false;
  mocks.loadFailed = false;
  mocks.userPresent = true;
  mocks.securityPresent = true;
  mocks.enrollPassword.mockReset().mockResolvedValue(undefined);
  mocks.refresh.mockReset().mockResolvedValue(undefined);
});

describe("account security setup", () => {
  it("collects phone and a paste-friendly password only after verified account creation", async () => {
    renderSetup();
    expect(screen.getByRole("heading", { name: "Secure your account" })).toBeVisible();
    expect(screen.getByLabelText("Phone number")).toHaveAttribute("autocomplete", "tel");
    expect(screen.getByLabelText("Password")).toHaveAttribute("autocomplete", "new-password");
    expect(screen.getByLabelText("Confirm password")).toHaveAttribute("autocomplete", "new-password");
    expect(screen.getByRole("button", { name: "Show password" })).toBeVisible();

    await userEvent.type(screen.getByLabelText("Phone number"), "0911111111");
    await userEvent.type(screen.getByLabelText("Password"), "correct horse battery staple");
    await userEvent.type(screen.getByLabelText("Confirm password"), "correct horse battery staple");
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));

    expect(mocks.enrollPassword).toHaveBeenCalledWith(
      "0911111111", "correct horse battery staple", "correct horse battery staple");
    expect(await screen.findByRole("heading", { name: "PIN setup" })).toBeVisible();
  });

  it("does not collect the phone twice and blocks mismatched passwords", async () => {
    mocks.phoneEnrolled = true;
    renderSetup();
    expect(screen.queryByLabelText("Phone number")).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText("Password"), "correct horse battery staple");
    await userEvent.type(screen.getByLabelText("Confirm password"), "different secure passphrase");
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Passwords don't match");
    expect(mocks.enrollPassword).not.toHaveBeenCalled();
  });

  it("shows a branded loading state instead of a blank setup page", () => {
    mocks.loading = true;
    renderSetup();
    expect(screen.getByRole("status")).toHaveTextContent("Opening your account setup");
    expect(screen.getByRole("img", { name: "Weymela" })).toBeVisible();
    expect(screen.queryByLabelText("Password")).not.toBeInTheDocument();
  });

  it("shows a recoverable non-technical failure and retries bootstrap", async () => {
    mocks.loadFailed = true;
    mocks.userPresent = false;
    mocks.securityPresent = false;
    renderSetup();
    expect(screen.getByRole("heading")).toHaveTextContent("couldn't load your account setup");
    expect(screen.queryByText(/Firebase|session|token|binding|API|database/i)).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(mocks.refresh).toHaveBeenCalledOnce();
  });

  it("fails safely when authenticated account-security state is unavailable", () => {
    mocks.securityPresent = false;
    renderSetup();
    expect(screen.getByRole("button", { name: "Try again" })).toBeVisible();
    expect(screen.queryByLabelText("Password")).not.toBeInTheDocument();
  });
});
