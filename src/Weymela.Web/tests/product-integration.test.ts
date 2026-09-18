import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import {
  productHandoffFailureRecovery,
  productWorkspaceMismatchPath,
} from "../src/app/ProductIntegration";

const source = readFileSync(resolve("src/app/ProductIntegration.tsx"), "utf8");
const routes = readFileSync(resolve("src/app/App.tsx"), "utf8");

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

  it("keeps the authorization-code exchange in fetches and changes history only at the final route", () => {
    expect(source).toContain('"X-Weymela-Product-Request": "1"');
    expect(source).toContain('credentials: "same-origin"');
    expect(source).toContain('redirect: "error"');
    expect(source).toContain("location.replace(expected)");
    expect(source).toContain('purpose === "PROFILE_ONBOARDING" && preserveProfileSelection');
    expect(source).toContain("location.assign(expected)");
    expect(source).not.toContain("form.submit()");
    expect(source).not.toContain("Opening your Weymela workspace");
  });

  it("renders product entry routes outside the legacy V3 workspace shell", () => {
    for (const path of ["/customer/offers", "/admin"])
      expect(routes).toContain(`<Route path="${path}" element={<RoleGate`);
    for (const role of ["Creator", "Business"])
      expect(routes).toContain(`<ProductWorkspaceEntry role="${role}"`);
    expect(routes).not.toContain('<Route path="/creator" element={<RoleGate');
    expect(routes).not.toContain('<Route path="/business" element={<RoleGate');
    expect(routes).not.toMatch(/<Route path="\/admin" element={<ProductWorkspaceEntry/);
  });

  it("separates profile onboarding from unauthorized workspace access", () => {
    expect(productWorkspaceMismatchPath(undefined, "Business")).toBe("/sign-in");
    expect(productWorkspaceMismatchPath("Customer", "Creator")).toBe(
      "/onboarding",
    );
    expect(productWorkspaceMismatchPath("Business", "Creator")).toBe(
      "/onboarding",
    );
    expect(productWorkspaceMismatchPath("Cashier", "Business")).toBe(
      "/unauthorized",
    );
    expect(productWorkspaceMismatchPath("OperationsAdmin", "Business")).toBe(
      "/unauthorized",
    );
  });
});
