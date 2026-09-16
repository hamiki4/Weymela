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

function pinCells(label: string) {
  return Array.from(screen.getByRole("group", { name: label }).querySelectorAll("input"));
}

beforeEach(() => mocks.enrollDevice.mockReset().mockResolvedValue(undefined));

describe("initial five-digit PIN setup", () => {
  it("renders masked mobile-friendly accessible PIN fields", () => {
    renderSetup();
    expect(screen.getByRole("heading", { name: "Create your PIN" })).toBeVisible();
    for (const label of ["Create PIN", "Confirm PIN"]) {
      const inputs = pinCells(label);
      expect(inputs).toHaveLength(5);
      inputs.forEach(input => {
        expect(input.type).toBe("password");
        expect(input.inputMode).toBe("numeric");
        expect(input.maxLength).toBe(1);
      });
    }
  });

  it("rejects malformed and mismatched PINs without calling the API", async () => {
    renderSetup();
    const user = userEvent.setup();
    await user.type(pinCells("Create PIN")[0], "1234a");
    await user.type(pinCells("Confirm PIN")[0], "1234a");
    await user.click(screen.getByRole("button", { name: "Continue" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Enter five digits");
    expect(mocks.enrollDevice).not.toHaveBeenCalled();

    await user.type(pinCells("Create PIN")[4], "5");
    await user.type(pinCells("Confirm PIN")[4], "6");
    await user.click(screen.getByRole("button", { name: "Continue" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("don't match");
    expect(mocks.enrollDevice).not.toHaveBeenCalled();
  });

  it("enrolls once and returns to the existing active role workspace", async () => {
    renderSetup();
    const user = userEvent.setup();
    await user.type(pinCells("Create PIN")[0], "01234");
    await user.type(pinCells("Confirm PIN")[0], "01234");
    await user.click(screen.getByRole("button", { name: "Continue" }));
    expect(mocks.enrollDevice).toHaveBeenCalledWith("01234", "01234");
    expect(await screen.findByRole("heading", { name: "Customer workspace" })).toBeVisible();
  });
});
