import { describe, expect, it } from "vitest";
import { money } from "../src/ui/format";

describe("context-aware money display", () => {
  it("keeps compact product amounts free of repeated currency labels", () => {
    expect(money(10_000)).toBe("10,000");
    expect(money(750)).toBe("750");
  });

  it("retains explicit currency in financial contexts", () => {
    const explicit = money(3_000, "explicit", "ETB");
    expect(explicit).toContain("ETB");
    expect(explicit).toContain("3,000");
  });
});
