import { describe, expect, it } from "vitest";
import { accountEntryPath } from "../src/app/AccountEntry";

const user = {
  role: "Onboarding" as const,
  displayName: "Account setup",
  publicId: "",
  developmentMode: false,
  canCheckout: false,
  profiles: [],
  activeProfileKey: null,
};

describe("account lifecycle entry", () => {
  it("resumes the first incomplete required account step", () => {
    expect(accountEntryPath(user,
      { passwordEnrolled: false, phoneEnrolled: false }, null)).toBe("/security-setup");
    expect(accountEntryPath(user,
      { passwordEnrolled: true, phoneEnrolled: true },
      { state: "EnrollmentRequired", expiresAtUtc: null })).toBe("/pin-setup");
    expect(accountEntryPath(user,
      { passwordEnrolled: true, phoneEnrolled: true },
      { state: "Enrolled", expiresAtUtc: null })).toBe("/onboarding");
  });

  it("returns an established account to its current workspace", () => {
    expect(accountEntryPath({ ...user, role: "Customer" },
      { passwordEnrolled: true, phoneEnrolled: true },
      { state: "Enrolled", expiresAtUtc: null })).toBe("/customer/offers");
  });
});
