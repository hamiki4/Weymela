import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const mocks = vi.hoisted(() => ({ post: vi.fn().mockResolvedValue({ id: "enrollment-1" }), reload: vi.fn() }));
vi.mock("../src/api/client", () => ({
  post: mocks.post,
  useAction: () => ({ busy: false, error: null, run: async (fn: (key: string) => Promise<void>) => fn("enroll-key") }),
  useResource: () => ({ data: { profiles: [] }, loading: false, error: null, reload: mocks.reload }),
}));
vi.mock("../src/app/Session", () => ({
  useSession: () => ({ user: { role: "Onboarding", displayName: "Account setup", publicId: "", profiles: [] }, refresh: vi.fn(), signOut: vi.fn() }),
}));

import { Onboarding } from "../src/app/Onboarding";

describe("additional profile onboarding", () => {
  it("offers only public roles and submits a Creator request without inventing a membership", async () => {
    render(<Onboarding />);
    expect(screen.getByRole("button", { name: /Become a Creator/ })).toBeVisible();
    expect(screen.getByRole("button", { name: /Add a Business/ })).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: /Become a Creator/ }));
    await userEvent.type(screen.getByLabelText("Display name"), "Bella");
    await userEvent.type(screen.getByLabelText("Public ID"), "CR-1");
    await userEvent.click(screen.getByRole("button", { name: "Submit for review" }));
    expect(mocks.post).toHaveBeenCalledWith("/onboarding/profile", expect.objectContaining({ role: "Creator", displayName: "Bella", publicId: "CR-1" }), "enroll-key");
  });

  it("activates a Customer without a review action", async () => {
    render(<Onboarding />);
    await userEvent.click(screen.getByRole("button", { name: /Use as Customer/ }));
    await userEvent.type(screen.getByLabelText("Display name"), "Hana");
    await userEvent.type(screen.getByLabelText("Public ID"), "CU-1");
    expect(screen.getByRole("button", { name: "Continue" })).toBeVisible();
    expect(screen.queryByRole("button", { name: "Submit for review" })).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));
    expect(mocks.post).toHaveBeenLastCalledWith("/onboarding/profile", expect.objectContaining({ role: "Customer", displayName: "Hana", publicId: "CU-1" }), "enroll-key");
  });
});
