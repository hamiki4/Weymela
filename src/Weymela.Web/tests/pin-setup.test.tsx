import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";

const mocks = vi.hoisted(() => ({ enrollDevice: vi.fn() }));
vi.mock("../src/app/Session", async importOriginal => {
  const original = await importOriginal<typeof import("../src/app/Session")>();
  return {
    ...original,
    useSession: () => ({
      user: { role: "Customer", displayName: "Hana", publicId: "CU-1", profiles: [] },
      loading: false,
      deviceEnrollment: { state: "EnrollmentRequired", expiresAtUtc: null },
      enrollDevice: mocks.enrollDevice,
    }),
  };
});

import { PinSetup } from "../src/app/PinSetup";

function renderSetup() {
  render(<MemoryRouter initialEntries={["/pin-setup"]}><Routes>
    <Route path="/pin-setup" element={<PinSetup />} />
    <Route path="/customer/offers" element={<h1>Customer workspace</h1>} />
  </Routes></MemoryRouter>);
}

beforeEach(() => mocks.enrollDevice.mockReset().mockResolvedValue(undefined));

describe("initial five-digit PIN setup", () => {
  it("renders masked mobile-friendly accessible PIN fields", () => {
    renderSetup();
    expect(screen.getByRole("heading", { name: "Set up your Weymela PIN" })).toBeVisible();
    for (const label of ["5-digit PIN", "Confirm 5-digit PIN"]) {
      const input = screen.getByLabelText(label) as HTMLInputElement;
      expect(input.type).toBe("password");
      expect(input.inputMode).toBe("numeric");
      expect(input.maxLength).toBe(5);
    }
  });

  it("rejects malformed and mismatched PINs without calling the API", async () => {
    renderSetup();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("5-digit PIN"), "1234a");
    await user.type(screen.getByLabelText("Confirm 5-digit PIN"), "1234a");
    await user.click(screen.getByRole("button", { name: "Continue" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("exactly five digits");
    expect(mocks.enrollDevice).not.toHaveBeenCalled();

    await user.clear(screen.getByLabelText("5-digit PIN"));
    await user.clear(screen.getByLabelText("Confirm 5-digit PIN"));
    await user.type(screen.getByLabelText("5-digit PIN"), "12345");
    await user.type(screen.getByLabelText("Confirm 5-digit PIN"), "54321");
    await user.click(screen.getByRole("button", { name: "Continue" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("do not match");
    expect(mocks.enrollDevice).not.toHaveBeenCalled();
  });

  it("enrolls once and returns to the existing active role workspace", async () => {
    renderSetup();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("5-digit PIN"), "01234");
    await user.type(screen.getByLabelText("Confirm 5-digit PIN"), "01234");
    await user.click(screen.getByRole("button", { name: "Continue" }));
    expect(mocks.enrollDevice).toHaveBeenCalledWith("01234", "01234");
    expect(await screen.findByRole("heading", { name: "Customer workspace" })).toBeVisible();
  });
});
