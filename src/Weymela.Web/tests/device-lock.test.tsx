import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const mocks = vi.hoisted(() => ({ unlockDevice: vi.fn(), signOut: vi.fn(), state: "Locked" as string }));
vi.mock("../src/app/Session", () => ({
  useSession: () => ({
    user: { role: "Customer", displayName: "Hana", publicId: "CU-1", profiles: [] },
    loading: false,
    deviceAccess: { state: mocks.state, idleExpiresAtUtc: null, sessionExpiresAtUtc: null, retryAfterSeconds: null },
    unlockDevice: mocks.unlockDevice,
    signOut: mocks.signOut,
  }),
}));

import { LockScreen } from "../src/app/LockScreen";

beforeEach(() => {
  mocks.state = "Locked";
  mocks.unlockDevice.mockReset().mockResolvedValue(undefined);
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
    expect(screen.getByRole("button", { name: "Sign out" })).toBeVisible();
  });

  it("does not claim Forgot PIN works before verified-email recovery is implemented", async () => {
    render(<LockScreen />);
    await userEvent.click(screen.getByRole("button", { name: "Forgot PIN" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("not available yet");
  });
});
