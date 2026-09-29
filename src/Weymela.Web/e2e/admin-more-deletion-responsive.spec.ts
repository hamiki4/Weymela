import { expect, test } from "@playwright/test";
import { layout, login, open } from "./helpers";

test("Admin More is a routed full page at phone widths with working links and Back", async ({ page, context }) => {
  await login(context, "admin");
  for (const width of [320, 360, 375, 390, 430]) {
    await page.setViewportSize({ width, height: 844 });
    await open(page, "/admin/wallets");
    await page.getByRole("navigation", { name: "Mobile navigation" }).getByRole("link", { name: "More navigation" }).click();
    await expect(page).toHaveURL(/\/admin\/more$/);
    await expect(page.getByRole("main").getByRole("heading", { name: "More" })).toBeVisible();
    await expect(page.getByRole("dialog", { name: "More navigation" })).toHaveCount(0);
    for (const [name, path] of Object.entries({ Customers: "/admin/customers", Creators: "/admin/creators",
      Businesses: "/admin/businesses", Admins: "/admin/admins", Campaigns: "/admin/campaigns",
      UGC: "/admin/ugc", Reports: "/admin/reports", "Financial Settings": "/admin/settings",
      Notifications: "/notifications" }))
      await expect(page.getByRole("main").getByRole("link", { name })).toHaveAttribute("href", path);
    await layout(page);
    await page.getByRole("main").getByRole("link", { name: "Back" }).click();
    await expect(page).toHaveURL(/\/admin\/wallets$/);
    await expect(page.getByRole("navigation", { name: "Mobile navigation" })).toBeVisible();
    await layout(page);
  }
});

test("Platform Admin role menu and identity deletion confirmation remain compact", async ({ page, context }) => {
  await login(context, "admin");
  for (const width of [320, 360, 375, 390, 430]) {
    await page.setViewportSize({ width, height: 844 });
    await open(page, "/admin/businesses");
    const row = page.locator(".admin-mobile-row").first();
    await expect(row).toBeVisible();
    await row.locator(".admin-action-menu summary").click();
    await expect(row.getByRole("menuitem", { name: "Delete Role" })).toBeVisible();
    await layout(page);
    await row.getByRole("menuitem", { name: "Delete Role" }).click();
    const roleDialog = page.getByRole("dialog", { name: /Delete Role/ });
    await expect(roleDialog.getByText(/permanently removes this Business profile/)).toBeVisible();
    await expect(roleDialog.getByRole("button", { name: "Delete Role" })).toBeDisabled();
    await layout(page);
    await roleDialog.getByRole("button", { name: "Cancel" }).click();
    await row.getByRole("link").first().click();
    await expect(page).toHaveURL(/\/admin\/accounts\//);
    await page.getByRole("button", { name: "Delete Entire Account" }).click();
    const entire = page.getByRole("dialog", { name: "Delete Entire Weymela Account?" });
    await expect(entire.getByRole("button", { name: "Delete Entire Account" })).toBeDisabled();
    await entire.getByLabel("Deletion reason").fill("Pilot test cleanup");
    await entire.getByLabel("Type DELETE to confirm").fill("DELETE");
    await expect(entire.getByRole("button", { name: "Delete Entire Account" })).toBeEnabled();
    await layout(page);
    await entire.getByRole("button", { name: "Cancel" }).click();
  }
});
