import { test, expect } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";

const adminLinks = ["Dashboard", "Customers", "Creators", "Businesses", "Admins", "Campaigns", "Wallets", "UGC", "Payouts", "Reports", "Financial Settings", "Notifications"];

test("Platform Admin uses the compact navigation and every dashboard summary leads somewhere", async ({ page, context }) => {
  await login(context, "admin");
  await open(page, "/admin");
  const nav = page.getByRole("navigation", { name: "Main navigation" });
  for (const label of adminLinks) await expect(nav.getByRole("link", { name: label, exact: true })).toBeVisible();
  for (const retired of ["View As", "Audit", "Accounts", "Platform Revenue"]) await expect(nav.getByRole("link", { name: retired, exact: true })).toHaveCount(0);
  await expect(page.locator(".topbar-identity")).not.toBeEmpty();
  await expect(page.locator(".topbar-identity")).not.toContainText("@");
  await expect(page.getByRole("heading", { name: "Recent activity" })).toHaveCount(0);
  for (const [label, path] of [["Businesses", "/admin/businesses"], ["Creators", "/admin/creators"], ["Customers", "/admin/customers"], ["Active Campaigns", "/admin/campaigns"], ["Pending payouts", "/admin/payouts"], ["Pending deposits", "/admin/wallets"]]) {
    const link = page.locator(".admin-dashboard-links").getByRole("link", { name: new RegExp(label) });
    await expect(link).toHaveAttribute("href", path);
  }
  await layout(page);
  await screenshot(page, "admin-redesign-desktop-dashboard");
});

test("Wallets, UGC and Reports use authoritative projections and keep period totals distinct", async ({ page, context }) => {
  await login(context, "admin");
  await open(page, "/admin/wallets");
  await expect(page.getByRole("heading", { name: "Business wallets" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Active View / Sale promotions" })).toBeVisible();
  await expect(page.locator(".desktop-data").first().getByRole("columnheader", { name: "Pending Deposit" })).toBeVisible();
  await screenshot(page, "admin-redesign-desktop-wallets");
  await open(page, "/admin/ugc");
  await expect(page.getByRole("heading", { name: "UGC funding and activity" })).toBeVisible();
  if (await page.locator(".admin-ugc-row").count())
    await expect(page.locator(".admin-ugc-row").first().getByText("Fixed Creator pay")).toBeVisible();
  else await expect(page.getByRole("heading", { name: "No UGC promotions" })).toBeVisible();
  await screenshot(page, "admin-redesign-desktop-ugc");
  await open(page, "/admin/reports");
  await expect(page.getByRole("heading", { name: "Platform summary" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Current balances" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Promotion summary" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "UGC summary" })).toBeVisible();
  await page.getByRole("button", { name: "Custom" }).click();
  await page.getByLabel("From").fill("2050-01-01");
  await page.getByLabel("To").fill("2050-01-07");
  await page.getByRole("button", { name: "Apply" }).click();
  await expect(page.locator(".admin-period-label").first()).toContainText("2050");
  await layout(page);
  await screenshot(page, "admin-redesign-desktop-reports");
});

test("Operations Admin cannot enter Platform financial pages", async ({ page, context }) => {
  await login(context, "operations-admin");
  await open(page, "/admin/operations");
  const nav = page.getByRole("navigation", { name: "Main navigation" });
  for (const label of ["Wallets", "Reports", "Financial Settings", "Admins"]) await expect(nav.getByRole("link", { name: label, exact: true })).toHaveCount(0);
  for (const path of ["/admin/wallets", "/admin/reports", "/admin/settings"]) {
    expect((await context.request.get(`/api${path === "/admin/settings" ? "/admin/financial-settings" : path}`)).status()).toBe(403);
    await page.goto(path);
    await expect(page).toHaveURL(/\/unauthorized$/);
  }
  await open(page, "/admin/ugc");
  await expect(page.getByRole("heading", { name: "UGC", exact: true })).toBeVisible();
});

test("Platform Admin mobile pages use purpose-built rows and compact bottom navigation", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(context, "admin");
  for (const path of ["/admin", "/admin/customers", "/admin/wallets", "/admin/ugc", "/admin/reports", "/admin/payouts", "/admin/settings"]) {
    await open(page, path);
    await layout(page);
    await screenshot(page, `admin-redesign-mobile-${path.slice(1).replaceAll("/", "-")}`);
    if (["/admin/customers", "/admin/wallets", "/admin/reports"].includes(path)) {
      await expect(page.locator(".desktop-data").first()).not.toBeVisible();
      await expect(page.locator(".mobile-data").first()).toBeVisible();
    }
    if (path === "/admin/ugc") await expect(page.locator(".admin-ugc-row, .empty-state").first()).toBeVisible();
  }
  const bottom = page.getByRole("navigation", { name: "Mobile navigation" });
  await expect(bottom.getByRole("link", { name: "Dashboard" })).toBeVisible();
  await expect(bottom.getByRole("link", { name: "Wallets" })).toBeVisible();
  await expect(bottom.getByRole("link", { name: "Payouts" })).toBeVisible();
  await bottom.getByRole("button", { name: "More navigation" }).click();
  const menu = page.getByRole("dialog", { name: "More navigation" });
  await expect(menu.getByRole("link", { name: "Reports" })).toBeVisible();
  await menu.getByRole("link", { name: "Reports" }).click();
  await expect(page).toHaveURL(/\/admin\/reports$/);
  await expect(menu).not.toBeVisible();
});

test("interactive controls keep their colors, borders, opacity and shadow on hover", async ({ page, context }) => {
  await login(context, "admin");
  await open(page, "/admin/customers");
  const button = page.getByRole("link", { name: "+ Create Customer" });
  const values = async () => button.evaluate(element => {
    const style = getComputedStyle(element);
    return [style.backgroundColor, style.color, style.borderColor, style.opacity, style.boxShadow, style.transform];
  });
  const before = await values();
  await button.hover();
  expect(await values()).toEqual(before);
});
