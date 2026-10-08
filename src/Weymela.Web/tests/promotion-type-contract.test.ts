import { describe, expect, it } from "vitest";
import {
  campaignType,
  isViewAndSale,
  isViewOnly,
  promotionTypeLabel,
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

  it("uses the simple Promotion type labels in Business and Creator workspaces", () => {
    expect(promotionTypeLabel("ViewOnly")).toBe("View Only");
    expect(promotionTypeLabel("ViewPlusCommission")).toBe("View + Sale");
    expect(promotionTypeLabel("UGC")).toBe("UGC");
    expect(promotionTypeLabel("UGC + Sales")).toBe("UGC + Sale");
  });
});
