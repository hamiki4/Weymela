import { describe, expect, it } from "vitest";
import {
  campaignType,
  isViewAndSale,
  isViewOnly,
  promotionTypeCode,
  statusLabel,
} from "../src/ui/format";

describe("Promotion type API contract", () => {
  it.each([
    ["ViewOnly", "ViewOnly", "View Only"],
    ["View Only", "ViewOnly", "View Only"],
    ["ViewPlusCommission", "ViewPlusCommission", "View & Sale"],
    ["View & Sale", "ViewPlusCommission", "View & Sale"],
  ])("normalizes %s", (input, code, label) => {
    expect(promotionTypeCode(input)).toBe(code);
    expect(campaignType(input)).toBe(label);
    expect(isViewOnly(input)).toBe(code === "ViewOnly");
    expect(isViewAndSale(input)).toBe(code === "ViewPlusCommission");
  });

  it("uses the locked user-facing View & Sale terminology", () => {
    expect(statusLabel("ViewPlusCommission")).toBe("View & Sale");
    expect(campaignType("ViewPlusCommission")).not.toContain("Commission");
  });
});
