import { test, expect } from "@playwright/test";
import { layout, login, open } from "./helpers";

const id = (number: number) => `00000000-0000-4000-8000-${number.toString().padStart(12, "0")}`;

async function enterViewAs(
  page: import("@playwright/test").Page,
  context: import("@playwright/test").BrowserContext,
  target: { number: number; name: string; role: string; home: string; next: string },
) {
  await login(context, "admin");
  const detailPath = `/admin/accounts/${id(target.number)}?mode=view`;
  await open(page, detailPath);
  await expect(page.getByRole("button", { name: "View As", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "View As", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: `View as ${target.name}?` });
  await expect(dialog).toContainText(`read-only Admin View of this ${target.role}`);
  const startRequest = page.waitForRequest((request) => request.url().endsWith("/api/admin/view-as/start"));
  await dialog.getByRole("button", { name: "View As", exact: true }).click();
  const request = await startRequest;
  expect(request.postDataJSON()).toEqual({ viewedUserId: id(target.number) });
  await expect(page).toHaveURL(new RegExp(`${target.home.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}$`));
  await expect(page.getByRole("complementary", { name: "Admin View Mode" })).toContainText("ADMIN VIEW MODE");
  await expect(page.getByRole("complementary", { name: "Admin View Mode" })).toContainText(`Viewing: ${target.name}`);
  await expect(page.getByRole("complementary", { name: "Admin View Mode" })).toContainText(`Role: ${target.role}`);
  await expect(page.getByRole("button", { name: "Exit View Mode", exact: true })).toBeVisible();
  return { detailPath };
}

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

test.describe("PlatformAdmin B5 View As experience", () => {
  test("views Customer read-only, restores after refresh, and exits server-side", async ({ page, context }) => {
    const target = { number: 7, name: "Hana", role: "Customer", home: "/customer/offers", next: "/customer/discover" };
    const { detailPath } = await enterViewAs(page, context, target);
    await page.getByRole("link", { name: "Discover", exact: true }).click();
    await expect(page).toHaveURL(/\/customer\/discover$/);
    await expect(page.getByRole("complementary", { name: "Admin View Mode" })).toContainText("ADMIN VIEW MODE");
    await page.reload();
    await expect(page).toHaveURL(/\/customer\/discover$/);
    await expect(page.getByRole("complementary", { name: "Admin View Mode" })).toContainText("Viewing: Hana");
    await page.getByRole("button", { name: "Exit View Mode", exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`${detailPath.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}$`));
    await expect(page.getByRole("complementary", { name: "Admin View Mode" })).toHaveCount(0);
    await expect(page.getByRole("link", { name: "Accounts", exact: true })).toBeVisible();
  });

  test("routes Creator, Business, and Operations Admin to their existing workspaces", async ({ page, context }) => {
    for (const target of [
      { number: 4, name: "Bella", role: "Creator", home: "/creator", next: "/creator/discover" },
      { number: 2, name: "Abc Coffee", role: "Business", home: "/business", next: "/business/campaigns" },
      { number: 11, name: "Weymela account", role: "Operations Admin", home: "/admin/operations", next: "/admin/businesses" },
    ]) {
      await enterViewAs(page, context, target);
      await page.goto(target.next);
      await expect(page).toHaveURL(new RegExp(`${target.next.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}$`));
      await expect(page.getByRole("complementary", { name: "Admin View Mode" })).toContainText("ADMIN VIEW MODE");
      if (target.role === "Operations Admin") {
        await expect(page.getByRole("link", { name: "Accounts", exact: true })).toHaveCount(0);
        await expect(page.getByRole("link", { name: "Financial Settings", exact: true })).toHaveCount(0);
        await expect(page.getByRole("link", { name: "Platform Revenue", exact: true })).toHaveCount(0);
      }
      await page.getByRole("button", { name: "Exit View Mode", exact: true }).click();
      await expect(page).toHaveURL(/\/admin\/accounts\/.*\?mode=view$/);
      await expect(page.getByRole("complementary", { name: "Admin View Mode" })).toHaveCount(0);
    }
  });

  test("does not offer View As for Platform Admin or Cashier accounts", async ({ page, context }) => {
    await login(context, "admin");
    for (const number of [1, 9]) {
      await open(page, `/admin/accounts/${id(number)}?mode=view`);
      await expect(page.getByRole("button", { name: "View As", exact: true })).toHaveCount(0);
    }
  });
});
