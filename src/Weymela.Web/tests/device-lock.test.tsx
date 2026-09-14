import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

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

beforeEach(() => {
  mocks.state = "Locked";
  mocks.unlockDevice.mockReset().mockResolvedValue(undefined);
  mocks.startPinRecovery.mockReset().mockResolvedValue({ accepted: true, expiresAtUtc: "2026-09-14T12:10:00Z", resendAfterSeconds: 60 });
  mocks.completePinRecovery.mockReset().mockResolvedValue(undefined);
  mocks.signOut.mockReset().mockResolvedValue(undefined);
});

describe("server-authoritative device lock", () => {
  it("renders a masked mobile PIN control and unlocks only with exactly five digits", async () => {
    render(<LockScreen />);
    const input = screen.getByLabelText("5-digit PIN") as HTMLInputElement;
    expect(input.type).toBe("password");
    expect(input.inputMode).toBe("numeric");
    expect(input.maxLength).toBe(5);
    await userEvent.type(input, "1234");
    await userEvent.click(screen.getByRole("button", { name: "Unlock" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("five-digit PIN");
    expect(mocks.unlockDevice).not.toHaveBeenCalled();
    await userEvent.clear(input);
    await userEvent.type(input, "01234");
    await userEvent.click(screen.getByRole("button", { name: "Unlock" }));
    expect(mocks.unlockDevice).toHaveBeenCalledWith("01234");
  });

  it.each([
    ["Cooldown", "Too many PIN attempts"],
    ["RecoveryRequired", "Verified-email PIN recovery is required"],
    ["FullAuthenticationRequired", "device session expired"],
  ])("renders truthful %s state without a usable PIN form", (state, message) => {
    mocks.state = state;
    render(<LockScreen />);
    expect(screen.getByRole("alert")).toHaveTextContent(message);
    expect(screen.queryByLabelText("5-digit PIN")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: state === "FullAuthenticationRequired" ? "Sign in fully" : "Sign out" })).toBeVisible();
  });

  it("starts generic verified-email recovery from a recognized locked device", async () => {
    render(<LockScreen />);
    await userEvent.click(screen.getByRole("button", { name: "Forgot PIN" }));
    expect(screen.getByRole("heading", { name: "Recover your Weymela PIN" })).toBeVisible();
    await userEvent.type(screen.getByLabelText("Phone number or registered email"), "+251900000000");
    await userEvent.click(screen.getByRole("button", { name: "Send recovery code" }));
    expect(mocks.startPinRecovery).toHaveBeenCalledWith("+251900000000");
    expect(await screen.findByLabelText("Email recovery code")).toHaveAttribute("inputmode", "numeric");
    expect(screen.getByText(/If the account is eligible/)).toBeVisible();
  });

  it("requires a six-digit code and matching masked five-digit PINs", async () => {
    render(<LockScreen />);
    await userEvent.click(screen.getByRole("button", { name: "Forgot PIN" }));
    await userEvent.type(screen.getByLabelText("Phone number or registered email"), "owner@example.test");
    await userEvent.click(screen.getByRole("button", { name: "Send recovery code" }));
    const newPin = screen.getByLabelText("New 5-digit PIN") as HTMLInputElement;
    const confirmation = screen.getByLabelText("Confirm new 5-digit PIN") as HTMLInputElement;
    expect(newPin.type).toBe("password");
    expect(confirmation.type).toBe("password");
    await userEvent.type(screen.getByLabelText("Email recovery code"), "246810");
    await userEvent.type(newPin, "12345");
    await userEvent.type(confirmation, "54321");
    await userEvent.click(screen.getByRole("button", { name: "Recover device" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("do not match");
    expect(mocks.completePinRecovery).not.toHaveBeenCalled();
    await userEvent.clear(confirmation);
    await userEvent.type(confirmation, "12345");
    await userEvent.click(screen.getByRole("button", { name: "Recover device" }));
    expect(mocks.completePinRecovery).toHaveBeenCalledWith("owner@example.test", "246810", "12345", "12345");
  });

  it.each(["Locked", "Cooldown", "RecoveryRequired"])("offers recovery in the %s state", async state => {
    mocks.state = state;
    render(<LockScreen />);
    await userEvent.click(screen.getByRole("button", { name: "Forgot PIN" }));
    expect(screen.getByLabelText("Phone number or registered email")).toBeVisible();
  });

  it("requires full sign-in rather than offering recovery from an expired device session", () => {
    mocks.state = "FullAuthenticationRequired";
    render(<LockScreen />);
    expect(screen.queryByRole("button", { name: "Forgot PIN" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign in fully" })).toBeVisible();
  });
});
