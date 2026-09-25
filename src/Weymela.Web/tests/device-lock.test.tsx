import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError } from "../src/api/client";
import { MemoryRouter } from "react-router-dom";

const mocks = vi.hoisted(() => ({
  unlockDevice: vi.fn(),
  startPinRecovery: vi.fn(),
  completePinRecovery: vi.fn(),
  signOut: vi.fn(),
  state: "Locked" as string,
}));
vi.mock("../src/app/Session", () => ({
  useSession: () => ({
    user: { role: "Customer", displayName: "Hana", publicId: "CU-1", profiles: [] },
    loading: false,
    deviceAccess: { state: mocks.state, idleExpiresAtUtc: null, sessionExpiresAtUtc: null, retryAfterSeconds: null },
    unlockDevice: mocks.unlockDevice,
    startPinRecovery: mocks.startPinRecovery,
    completePinRecovery: mocks.completePinRecovery,
    signOut: mocks.signOut,
  }),
}));

import { LockScreen } from "../src/app/LockScreen";

function pinCells(label: string) {
  return Array.from(screen.getByRole("group", { name: label }).querySelectorAll("input"));
}
function renderLock() { return render(<MemoryRouter><LockScreen /></MemoryRouter>); }

beforeEach(() => {
  mocks.state = "Locked";
  mocks.unlockDevice.mockReset().mockResolvedValue(undefined);
  mocks.startPinRecovery.mockReset().mockResolvedValue({ accepted: true, expiresAtUtc: "2026-09-14T12:10:00Z", resendAfterSeconds: 60 });
  mocks.completePinRecovery.mockReset().mockResolvedValue(undefined);
  mocks.signOut.mockReset().mockResolvedValue(undefined);
});

describe("server-authoritative device lock", () => {
  it("renders a masked mobile PIN control and unlocks only with exactly five digits", async () => {
    renderLock();
    const inputs = pinCells("PIN");
    expect(inputs).toHaveLength(5);
    inputs.forEach(input => {
      expect(input.type).toBe("password");
      expect(input.inputMode).toBe("numeric");
      expect(input.maxLength).toBe(1);
    });
    await userEvent.type(inputs[0], "1234");
    await userEvent.click(screen.getByRole("button", { name: "Unlock" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("five-digit PIN");
    expect(mocks.unlockDevice).not.toHaveBeenCalled();
    for (const input of inputs) await userEvent.clear(input);
    await userEvent.type(inputs[0], "01234");
    await userEvent.click(screen.getByRole("button", { name: "Unlock" }));
    expect(mocks.unlockDevice).toHaveBeenCalledWith("01234");
  });

  it.each([
    ["Cooldown", "Too many PIN attempts"],
    ["RecoveryRequired", "Verify your email to reset your PIN"],
    ["FullAuthenticationRequired", "Sign in again to continue"],
  ])("renders truthful %s state without a usable PIN form", (state, message) => {
    mocks.state = state;
    renderLock();
    expect(screen.getByRole("alert")).toHaveTextContent(message);
    expect(screen.queryByRole("group", { name: "PIN" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign in" })).toBeVisible();
  });

  it("starts generic verified-email recovery from a recognized locked device", async () => {
    renderLock();
    await userEvent.click(screen.getByRole("button", { name: "Forgot PIN?" }));
    expect(screen.getByRole("heading", { name: "Reset your PIN" })).toBeVisible();
    await userEvent.type(screen.getByLabelText("Email address"), "owner@example.test");
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));
    expect(mocks.startPinRecovery).toHaveBeenCalledWith("owner@example.test");
    expect(await screen.findByLabelText("Verification code")).toHaveAttribute("inputmode", "numeric");
    expect(screen.getByText(/If the account is eligible/)).toBeVisible();
  });

  it("requires a six-digit code and matching masked five-digit PINs", async () => {
    renderLock();
    await userEvent.click(screen.getByRole("button", { name: "Forgot PIN?" }));
    await userEvent.type(screen.getByLabelText("Email address"), "owner@example.test");
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));
    const newPin = pinCells("New PIN");
    const confirmation = pinCells("Confirm new PIN");
    expect(newPin).toHaveLength(5);
    expect(confirmation).toHaveLength(5);
    expect(newPin[0].type).toBe("password");
    expect(confirmation[0].type).toBe("password");
    await userEvent.type(screen.getByLabelText("Verification code"), "246810");
    await userEvent.type(newPin[0], "12345");
    await userEvent.type(confirmation[0], "54321");
    await userEvent.click(screen.getByRole("button", { name: "Recover device" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("don't match");
    expect(mocks.completePinRecovery).not.toHaveBeenCalled();
    for (const input of confirmation) await userEvent.clear(input);
    await userEvent.type(confirmation[0], "12345");
    await userEvent.click(screen.getByRole("button", { name: "Recover device" }));
    expect(mocks.completePinRecovery).toHaveBeenCalledWith("owner@example.test", "246810", "12345", "12345");
  });

  it("announces an invalid email code without falsely marking PIN cells invalid", async () => {
    mocks.completePinRecovery.mockRejectedValueOnce(new ApiError(400, "Invalid code", "InvalidCode"));
    renderLock();
    await userEvent.click(screen.getByRole("button", { name: "Forgot PIN?" }));
    await userEvent.type(screen.getByLabelText("Email address"), "owner@example.test");
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));
    await userEvent.type(screen.getByLabelText("Verification code"), "246810");
    await userEvent.type(pinCells("New PIN")[0], "12345");
    await userEvent.type(pinCells("Confirm new PIN")[0], "12345");
    await userEvent.click(screen.getByRole("button", { name: "Recover device" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("code is invalid or expired");
    expect(pinCells("New PIN")[0]).toHaveAttribute("aria-invalid", "false");
  });

  it.each(["Locked", "Cooldown", "RecoveryRequired"])("offers recovery in the %s state", async state => {
    mocks.state = state;
    renderLock();
    await userEvent.click(screen.getByRole("button", { name: "Forgot PIN?" }));
    expect(screen.getByLabelText("Email address")).toBeVisible();
  });

  it("requires full sign-in rather than offering recovery from an expired device session", () => {
    mocks.state = "FullAuthenticationRequired";
    renderLock();
    expect(screen.queryByRole("button", { name: "Forgot PIN?" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign in" })).toBeVisible();
  });

  it("preserves wrong-PIN response and server cooldown without bypassing attempts", async () => {
    mocks.unlockDevice.mockRejectedValueOnce(new ApiError(401, "Incorrect PIN.", "InvalidPin"))
      .mockRejectedValueOnce(new ApiError(429, "Too many attempts.", "PinCooldown"));
    renderLock();
    const inputs = pinCells("PIN");
    await userEvent.type(inputs[0], "99999");
    await userEvent.click(screen.getByRole("button", { name: "Unlock" }));
    expect(mocks.unlockDevice).toHaveBeenCalledWith("99999");
    expect(await screen.findByRole("alert")).toHaveTextContent("Incorrect PIN");
    await userEvent.click(screen.getByRole("button", { name: "Unlock" }));
    expect(mocks.unlockDevice).toHaveBeenCalledTimes(2);
    expect(await screen.findByRole("alert")).toHaveTextContent("Too many PIN attempts");
  });

  it("keeps a slow unlock in one transition and ignores a duplicate PIN submit", async () => {
    let complete!: () => void;
    mocks.unlockDevice.mockImplementation(() => new Promise<void>(resolve => { complete = resolve; }));
    renderLock();
    const user = userEvent.setup();
    await user.type(pinCells("PIN")[0], "01234");
    const button = screen.getByRole("button", { name: "Unlock" });
    await user.click(button);
    expect(button).toBeDisabled();
    await user.click(button);
    expect(mocks.unlockDevice).toHaveBeenCalledOnce();
    expect(screen.queryByRole("heading", { name: "Sign In" })).not.toBeInTheDocument();
    complete();
  });
});
