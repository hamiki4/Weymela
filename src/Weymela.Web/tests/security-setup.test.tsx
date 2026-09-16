import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";

const mocks = vi.hoisted(() => ({
  enrollPassword: vi.fn(),
  phoneEnrolled: false,
}));

vi.mock("../src/app/Session", () => ({
  roleHome: { Customer: "/customer/offers" },
  useSession: () => ({
    loading: false,
    user: { role: "Customer" },
    accountSecurity: { passwordEnrolled: false, phoneEnrolled: mocks.phoneEnrolled },
    deviceEnrollment: { state: "EnrollmentRequired" },
    enrollPassword: mocks.enrollPassword,
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
  mocks.enrollPassword.mockReset().mockResolvedValue(undefined);
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
});
