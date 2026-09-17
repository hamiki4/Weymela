import { describe, expect, it } from "vitest";
import { productHandoffFailureRecovery } from "../src/app/ProductIntegration";

describe("product handoff recovery", () => {
  it("keeps Platform Admin failures on the privileged Admin path", () => {
    expect(productHandoffFailureRecovery("PlatformAdmin")).toEqual({
      label: "Retry Admin workspace",
      path: "/admin",
    });
  });

  it.each(["Customer", "Creator", "Business"])(
    "keeps %s failures on public profile recovery",
    (role) => {
      expect(productHandoffFailureRecovery(role)).toEqual({
        label: "Back to profiles",
        path: "/onboarding",
      });
    },
  );
});
