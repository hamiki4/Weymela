import { test, expect } from "@playwright/test";
import { layout, login, open } from "./helpers";

test("PlatformAdmin can inspect the account directory and preauthorize a Platform Admin", async ({ page, context }) => {
  await login(context, "admin");
  await open(page, "/admin/accounts");
  await expect(page.getByRole("heading", { name: "Accounts", exact: true })).toBeVisible();
  for (const tab of ["All", "Customers", "Creators", "Businesses", "Cashiers", "Operations Admins", "Platform Admins"])
    await expect(page.getByRole("tab", { name: tab, exact: true })).toBeVisible();
  await page.getByRole("button", { name: "+ Create Account" }).click();
  await page.getByLabel("Account type").selectOption("PlatformAdmin");
  await page.getByLabel("Email (required for a new identity)").fill(`browser-admin-${Date.now()}@example.com`);
  await page.getByLabel("Display name").fill("Browser Platform Admin");
  await page.getByLabel("Reason").fill("Browser security authority test");
  await page.getByRole("button", { name: "Create preauthorization" }).click();
  await expect(page.getByRole("status")).toContainText("Preauthorization created");
  await expect(page.locator(".activation-secret code")).toHaveCount(0);
  await expect(page.getByRole("status")).toContainText("Activation instructions were sent");
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

test("OperationsAdmin can use the operational workspace without Platform controls", async ({ page, context }) => {
  await login(context, "operations-admin");
  await open(page, "/admin/operations");
  await expect(page.getByRole("heading", { name: "Keep Weymela moving." })).toBeVisible();
  await expect(page.getByRole("link", { name: "Home", exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "Payouts", exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "Financial Settings", exact: true })).toHaveCount(0);
  await expect(page.getByRole("link", { name: "Platform Revenue", exact: true })).toHaveCount(0);
  await expect(page.getByRole("link", { name: "Accounts", exact: true })).toHaveCount(0);
  await layout(page);
});
