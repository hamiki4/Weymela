import { expect, test } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";

for (const width of [320, 360, 375, 390, 393, 430]) {
  test(`Settings and full Profile at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    for (const [role, home] of [["customer", "/customer/offers"], ["creator", "/creator"], ["business", "/business"]] as const) {
      await login(context, role);
      await open(page, home);
      await expect(page.getByRole("link", { name: "Your notifications" })).toBeVisible();
      await expect(page.getByRole("link", { name: "Open Settings" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Open account menu" })).toHaveCount(0);
      await page.getByRole("link", { name: "Open Settings" }).click();
      await expect(page).toHaveURL(/\/settings$/);
      const settings = page.locator("main .settings-page");
      await expect(settings).toBeVisible();
      await expect(settings.getByLabel("Switch profile")).toBeVisible();
      await expect(settings.getByRole("group", { name: "Language" })).toBeVisible();
      await expect(settings.getByRole("link", { name: "Help" })).toBeVisible();
      await expect(settings.getByRole("link", { name: "Contact Us" })).toBeVisible();
      await expect(settings.getByRole("link", { name: /Delete Account/ })).toBeVisible();
      await expect(settings.getByRole("button", { name: "Sign Out" })).toBeVisible();
      if (role === "creator") await expect(settings.getByRole("link", { name: "Social Profiles" })).toHaveAttribute("href", "/profile#social-profiles");
      if (role === "business") {
        await expect(settings.getByRole("link", { name: "Cashier Management" })).toBeVisible();
        await expect(settings.getByRole("link", { name: "Create Cashier" })).toHaveCount(0);
      }
      const settingsBox = await settings.boundingBox();
      expect(settingsBox).not.toBeNull();
      expect(settingsBox!.x).toBeGreaterThanOrEqual(0);
      expect(settingsBox!.x + settingsBox!.width).toBeLessThanOrEqual(width + 1);
      await layout(page);
      if (width === 390) await screenshot(page, `settings-${role}-390`);
      await settings.getByRole("link", { name: "Profile", exact: true }).click();
      await expect(page).toHaveURL(/\/profile$/);
      await expect(page.getByRole("heading", { name: "Profile", exact: true })).toHaveClass(/sr-only/);
      await expect(settings).toHaveCount(0);
      await layout(page);
      const nav = page.getByRole("navigation", { name: "Mobile navigation" });
      const navBox = await nav.boundingBox();
      expect(navBox).not.toBeNull();
      expect(navBox!.x + navBox!.width).toBeLessThanOrEqual(width + 1);
      expect(navBox!.y + navBox!.height).toBeLessThanOrEqual(845);
      if (role === "customer") await expect(page.getByRole("heading", { name: "Social Profiles" })).toHaveCount(0);
      if (role === "creator") await expect(page.getByRole("heading", { name: "Social Profiles" })).toBeVisible();
      if (role === "business") await expect(page.getByRole("heading", { name: "Business Information" })).toBeVisible();
      if (width === 390) await screenshot(page, `profile-${role}-390`);
      if (role === "business") {
        await open(page, "/business/wallet");
        await layout(page);
        if (width === 390) await screenshot(page, "business-wallet-390");
      }
    }
  });
}

test("Creator Settings opens Social Profiles on the Profile page", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(context, "creator");
  await open(page, "/creator");
  await page.getByRole("link", { name: "Open Settings" }).click();
  await page.locator("main .settings-page").getByRole("link", { name: "Social Profiles" }).click();
  await expect(page).toHaveURL(/\/profile#social-profiles$/);
  await expect(page.getByRole("heading", { name: "Social Profiles" })).toBeVisible();
  await layout(page);
  await screenshot(page, "creator-social-profiles-390");
});

test("Business Settings opens Cashier Management with its Create Cashier form", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(context, "business");
  await open(page, "/business");
  await page.getByRole("link", { name: "Open Settings" }).click();
  await page.locator("main .settings-page").getByRole("link", { name: "Cashier Management" }).click();
  await expect(page).toHaveURL(/\/business\/cashiers$/);
  await expect(page.getByRole("heading", { name: "Add Cashier" })).toBeVisible();
  await layout(page);
  await screenshot(page, "390-business-cashier-management");
});

test("Settings, role Help, Contact and account closure remain localized for every role", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  for (const role of ["customer", "creator", "business", "cashier", "operations-admin", "admin"]) {
    await login(context, role);
    await open(page, "/settings");
    await page.getByRole("button", { name: "አማርኛ" }).click();
    await expect(page.locator("html")).toHaveAttribute("lang", "am");
    await page.reload();
    await expect(page.locator("html")).toHaveAttribute("lang", "am");
    await layout(page);

    await page.getByRole("link", { name: "እገዛ", exact: true }).click();
    await expect(page.locator(".help-list h2")).toContainText("እገዛ");
    await layout(page);
    await page.getByRole("link", { name: "ተመለስ", exact: true }).click();

    await page.getByRole("link", { name: "ያግኙን", exact: true }).click();
    await expect(page.getByRole("link", { name: /support@weymela.com/ })).toHaveAttribute("href", "mailto:support@weymela.com");
    await expect(page.getByRole("link", { name: /\+251911111111/ })).toHaveAttribute("href", "tel:+251911111111");
    await page.getByRole("link", { name: "ተመለስ", exact: true }).click();

    await page.getByRole("link", { name: /መለያ ሰርዝ/ }).click();
    await expect(page.getByRole("heading", { name: "የመለያ ሚና ይምረጡ" })).toBeVisible();
    await layout(page);
    await page.getByRole("link", { name: "ተመለስ", exact: true }).click();
    await page.getByRole("button", { name: "English" }).click();
    await expect(page.locator("html")).toHaveAttribute("lang", "en");
  }
});
