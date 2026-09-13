import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { SessionProfile } from "../src/api/types";
import { ProfileSwitcher } from "../src/app/Shell";

const profiles: SessionProfile[] = [
  { role: "Creator", subjectId: "creator-1", businessId: null, displayName: "Bella", publicId: "CR-1", canCheckout: false },
  { role: "Business", subjectId: "business-1", businessId: "business-1", displayName: "ABC Café", publicId: "BUS-1", canCheckout: true },
];

describe("approved profile selector", () => {
  it("shows only approved profiles and requests a server switch for the selected membership", async () => {
    const onSwitch = vi.fn(async (profile: SessionProfile) => ({ role: profile.role }));
    const navigate = vi.fn();
    render(<ProfileSwitcher profiles={profiles} activeKey="Creator:creator-1:-" onSwitch={onSwitch} navigate={navigate as never} />);
    expect(screen.getByLabelText("Switch profile")).toBeVisible();
    await userEvent.selectOptions(screen.getByLabelText("Switch profile"), "Business:business-1:business-1");
    expect(onSwitch).toHaveBeenCalledWith(profiles[1]);
    expect(navigate).toHaveBeenCalledWith("/business", { replace: true });
  });
});
