import { afterEach, describe, expect, it, vi } from "vitest";
import { request } from "../src/api/client";

afterEach(() => vi.unstubAllGlobals());

describe("device enrollment prerequisite routing", () => {
  it("announces PIN setup when an API request discovers an unenrolled device", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => Response.json({
      code: "DeviceEnrollmentRequired",
      message: "Set up this device before opening a workspace.",
      state: "EnrollmentRequired",
    }, { status: 428 })));
    const state = new Promise<string | undefined>(resolve => window.addEventListener(
      "weymela-device-access",
      event => resolve((event as CustomEvent<{ state?: string }>).detail.state),
      { once: true },
    ));

    await expect(request("/customer/offers")).rejects.toMatchObject({
      code: "DeviceEnrollmentRequired",
      status: 428,
    });
    await expect(state).resolves.toBe("EnrollmentRequired");
  });
});
