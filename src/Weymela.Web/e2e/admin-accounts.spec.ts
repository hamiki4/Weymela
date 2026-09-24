import { test, expect } from "@playwright/test";
import { layout, login, open } from "./helpers";

test("PlatformAdmin can inspect the account directory and preauthorize an Operations Admin", async ({ page, context }) => {
  await login(context, "admin");
  await open(page, "/admin/accounts");
  await expect(page.getByRole("heading", { name: "Accounts", exact: true })).toBeVisible();
  for (const tab of ["All", "Customers", "Creators", "Businesses", "Cashiers", "Operations Admins", "Platform Admins"])
    await expect(page.getByRole("tab", { name: tab, exact: true })).toBeVisible();
  await page.getByRole("button", { name: "+ Create Account" }).click();
  await page.getByLabel("Account type").selectOption("OperationsAdmin");
  await page.getByLabel("Email (required for a new identity)").fill(`browser-admin-${Date.now()}@example.com`);
  await page.getByLabel("Display name").fill("Browser Operations Admin");
  await page.getByRole("button", { name: "Create preauthorization" }).click();
  await expect(page.getByRole("status")).toContainText("Preauthorization created");
  await expect(page.locator(".activation-secret code")).toHaveCount(1);
  await expect(page.locator("main")).not.toContainText(/password hash|pin verifier|firebase token/i);
  await layout(page);
});

test("OperationsAdmin cannot access the PlatformAdmin Accounts authority", async ({ page, context }) => {
  await login(context, "operations-admin");
  expect((await context.request.get("/api/admin/accounts")).status()).toBe(403);
  expect((await context.request.get("/api/admin/accounts/00000000-0000-0000-0000-000000000001")).status()).toBe(403);
  await page.goto("/admin/accounts");
  await expect(page).toHaveURL(/\/unauthorized$/);
  await expect(page.getByRole("heading", { name: /Workspace unavailable/i })).toBeVisible();
  await expect(page.locator("body")).not.toContainText("Account directory");
});
