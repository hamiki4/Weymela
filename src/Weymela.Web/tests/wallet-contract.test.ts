import { describe, expect, it } from "vitest";
import { validateWalletContract } from "../src/api/walletContract";
import { amount } from "../src/ui/format";

const wallet = { totalBalance: 10, available: 4, reserved: 6, history: [{ amount: -2 }] };

describe("authoritative Business wallet boundary", () => {
  it("accepts valid zero and nonzero amounts", () => {
    expect(() => validateWalletContract({ ...wallet, totalBalance: 0, available: 0, reserved: 0, history: [] })).not.toThrow();
    expect(() => validateWalletContract({ wallet })).not.toThrow();
    expect(amount(0)).not.toContain("NaN");
    expect(amount(10.25)).not.toContain("NaN");
  });

  it.each([undefined, null, "", "10", Number.NaN, Number.POSITIVE_INFINITY])(
    "rejects missing or invalid authoritative amount %s", value => {
      expect(() => validateWalletContract({ ...wallet, reserved: value })).toThrow(/unavailable/i);
      expect(amount(value as number)).not.toContain("NaN");
    },
  );

  it("rejects an inconsistent wallet instead of inventing a zero balance", () => {
    expect(() => validateWalletContract({ ...wallet, totalBalance: 11 })).toThrow(/inconsistent/i);
  });
});
