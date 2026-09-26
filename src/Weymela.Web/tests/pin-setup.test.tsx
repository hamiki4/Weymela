import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";

const mocks = vi.hoisted(() => ({
  enrollDevice: vi.fn(), refresh: vi.fn(), state: "ready" as "loading" | "failed" | "ready",
  deviceState: "EnrollmentRequired",
}));
vi.mock("../src/app/Session", async importOriginal => {
  const original = await importOriginal<typeof import("../src/app/Session")>();
  return {
    ...original,
    useSession: () => ({
      user: { role: "Customer", displayName: "Hana", publicId: "CU-1", profiles: [] },
      loading: mocks.state === "loading",
      loadFailed: mocks.state === "failed",
      accountSecurity: { passwordEnrolled: true, phoneEnrolled: true },
      deviceEnrollment: { state: mocks.deviceState, expiresAtUtc: null },
      enrollDevice: mocks.enrollDevice,
      refresh: mocks.refresh,
    }),
  };
});

import { PinSetup } from "../src/app/PinSetup";

function renderSetup() {
  return render(<MemoryRouter initialEntries={["/pin-setup"]}><Routes>
    <Route path="/pin-setup" element={<PinSetup />} />
    <Route path="/customer/offers" element={<h1>Customer workspace</h1>} />
  </Routes></MemoryRouter>);
}

function pinCells(label: string) {
  return Array.from(screen.getByRole("group", { name: label }).querySelectorAll("input"));
}

beforeEach(() => {
  mocks.state = "ready";
  mocks.deviceState = "EnrollmentRequired";
  mocks.enrollDevice.mockReset().mockResolvedValue(undefined);
  mocks.refresh.mockReset().mockResolvedValue(undefined);
});

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

  it("shows explicit loading and recoverable failure states instead of a blank route", async () => {
    mocks.state = "loading";
    const loading = renderSetup();
    expect(screen.getByRole("heading", { name: "Create your PIN" })).toBeVisible();
    expect(screen.getByRole("status")).toHaveTextContent("Preparing your secure session");
    expect(screen.getByRole("button", { name: "Continue" })).toBeDisabled();
    loading.unmount();

    mocks.state = "failed";
    renderSetup();
    expect(screen.getByRole("heading", { name: "We couldn't load your PIN setup." })).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(mocks.refresh).toHaveBeenCalledOnce();
  });

  it("offers retry when the device bootstrap is unavailable", async () => {
    mocks.deviceState = "Unavailable";
    renderSetup();
    expect(screen.getByRole("heading", { name: "We couldn't load your PIN setup." })).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(mocks.refresh).toHaveBeenCalledOnce();
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

  it("submits once and leaves the final destination to the authoritative app gate", async () => {
    renderSetup();
    const user = userEvent.setup();
    await user.type(pinCells("Create PIN")[0], "01234");
    await user.type(pinCells("Confirm PIN")[0], "01234");
    await user.click(screen.getByRole("button", { name: "Continue" }));
    expect(mocks.enrollDevice).toHaveBeenCalledWith("01234", "01234");
    expect(screen.queryByRole("heading", { name: "Customer workspace" })).not.toBeInTheDocument();
    expect(screen.queryByText(/Sign in|Welcome back/)).not.toBeInTheDocument();
  });

  it("keeps the setup transition stable and ignores a duplicate submit", async () => {
    let complete!: () => void;
    mocks.enrollDevice.mockImplementation(() => new Promise<void>(resolve => { complete = resolve; }));
    renderSetup();
    const user = userEvent.setup();
    await user.type(pinCells("Create PIN")[0], "01234");
    await user.type(pinCells("Confirm PIN")[0], "01234");
    const button = screen.getByRole("button", { name: "Continue" });
    await user.click(button);
    expect(button).toBeDisabled();
    await user.click(button);
    expect(mocks.enrollDevice).toHaveBeenCalledOnce();
    expect(screen.queryByRole("heading", { name: "Customer workspace" })).not.toBeInTheDocument();
    complete();
  });
});
