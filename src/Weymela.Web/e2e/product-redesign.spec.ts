import { expect, test } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";

test("Customer, Creator and Business use compact horizontal navigation on desktop", async ({ page, context }) => {
  await page.setViewportSize({ width: 1366, height: 900 });
  for (const [role, path, label] of [["customer", "/customer/offers", "Discover"], ["creator", "/creator", "Promotions"], ["business", "/business", "Wallet"]]) {
    await login(context, role);
    await open(page, path);
    await expect(page.locator(".product-desktop-nav").getByRole("link", { name: label, exact: true })).toBeVisible();
    await expect(page.locator(".sidebar")).not.toBeVisible();
    await layout(page);
    await screenshot(page, `product-desktop-${role}`);
  }
});

test("Customer mobile Home shows real account information and useful offer links", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(context, "customer");
  await open(page, "/customer/offers");
  await expect(page.getByRole("link", { name: /Available cashback/i })).toHaveAttribute("href", "/customer/cashback");
  const nav = page.getByRole("navigation", { name: "Mobile navigation" });
  await expect(nav.getByRole("link", { name: "Cashback" })).toBeVisible();
  await expect(nav.getByRole("link", { name: "Transactions" })).toBeVisible();
  await layout(page);
  await screenshot(page, "product-mobile-customer");
});

test("Creator Promotions uses three action-oriented groups", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(context, "creator");
  await open(page, "/creator/promotions");
  await expect(page.getByRole("heading", { name: "My Promotions" })).toBeVisible();
  const tabs = page.getByRole("tablist", { name: "Filter Promotions" });
  await expect(tabs.getByRole("tab")).toHaveCount(3);
  for (const name of ["Active", "Requests", "History"]) await expect(tabs.getByRole("tab", { name })).toBeVisible();
  await layout(page);
  await screenshot(page, "creator-promotions-mobile-final");
  await tabs.getByRole("tab", { name: "Requests" }).click();
  await expect(tabs.getByRole("tab", { name: "Requests" })).toHaveAttribute("aria-selected", "true");
  await layout(page);
  await tabs.getByRole("tab", { name: "History" }).click();
  await expect(tabs.getByRole("tab", { name: "History" })).toHaveAttribute("aria-selected", "true");
  await layout(page);
  await page.setViewportSize({ width: 1366, height: 768 });
  await open(page, "/creator/promotions");
  await layout(page);
  await screenshot(page, "creator-promotions-desktop-final");
});

test("Business keeps funding, Checkout and Cashier Management within reach", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(context, "business");
  await open(page, "/business");
  await expect(page.getByRole("link", { name: /Available funds/i })).toHaveAttribute("href", "/business/wallet");
  const nav = page.getByRole("navigation", { name: "Mobile navigation" });
  await expect(nav.getByRole("link", { name: "Wallet" })).toBeVisible();
  await nav.getByRole("button", { name: "Profile" }).click();
  const menu = page.getByRole("dialog", { name: "Account menu" });
  await expect(menu.getByRole("link", { name: "Cashier Management" })).toHaveAttribute("href", "/business/cashiers");
  await expect(menu.getByRole("link", { name: "Checkout" })).toHaveAttribute("href", "/checkout");
  await layout(page);
  await screenshot(page, "product-mobile-business");
});

test("Cashier has Purchase, Transactions and Profile without a dashboard", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(context, "cashier");
  await open(page, "/checkout");
  const nav = page.getByRole("navigation", { name: "Mobile navigation" });
  await expect(nav.getByRole("link", { name: "Purchase" })).toBeVisible();
  await nav.getByRole("link", { name: "Transactions" }).click();
  await expect(page).toHaveURL(/\/checkout\/transactions$/);
  await expect(page.getByRole("heading", { name: "Recent purchases" })).toBeVisible();
  await expect(nav.getByRole("button", { name: "Profile" })).toBeVisible();
  await layout(page);
  await screenshot(page, "product-mobile-cashier-transactions");
});
