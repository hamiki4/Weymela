import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const styles = readFileSync("src/styles.css", "utf8");
function channels(role: string): number[] {
  const hex = styles.match(new RegExp(`\\.role-${role} \\{ --role-accent: #([0-9a-f]{6})`))?.[1];
  if (!hex) throw new Error(`Missing ${role} accent`);
  return [0, 2, 4].map((offset) => parseInt(hex.slice(offset, offset + 2), 16));
}
function contrastOnWhite(rgb: number[]): number {
  const linear = rgb.map((value) => {
    const channel = value / 255;
    return channel <= .04045 ? channel / 12.92 : ((channel + .055) / 1.055) ** 2.4;
  });
  const luminance = linear[0] * .2126 + linear[1] * .7152 + linear[2] * .0722;
  return 1.05 / (luminance + .05);
}

describe("public role accents", () => {
  it("keeps readable green, purple and blue role accents", () => {
    const customer = channels("customer");
    const creator = channels("creator");
    const business = channels("business");
    expect(customer[1]).toBeGreaterThan(customer[0]);
    expect(customer[1]).toBeGreaterThan(customer[2]);
    expect(creator[0]).toBeGreaterThan(creator[1]);
    expect(creator[2]).toBeGreaterThan(creator[1]);
    expect(business[2]).toBeGreaterThan(business[0]);
    expect(business[2]).toBeGreaterThan(business[1]);
    for (const rgb of [customer, creator, business]) expect(contrastOnWhite(rgb)).toBeGreaterThanOrEqual(4.5);
    expect(styles).toContain(".app-shell .nav-link.active");
    expect(styles).toContain(".mobile-role-link.active");
  });
});
