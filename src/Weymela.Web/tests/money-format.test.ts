import { describe, expect, it } from "vitest";
import { money } from "../src/ui/format";

describe("context-aware money display", () => {
  it("keeps compact product amounts free of repeated currency labels", () => {
    expect(money(10_000)).toBe("10,000");
    expect(money(750)).toBe("750");
  });

  it("keeps financial amounts numeric in every display context", () => {
    const explicit = money(3_000, "explicit", "legacy-code");
    expect(explicit).toBe("3,000");
  });
});
