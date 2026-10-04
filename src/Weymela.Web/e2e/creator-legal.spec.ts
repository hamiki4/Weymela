import { expect, test } from "@playwright/test";
import { layout, login, open } from "./helpers";

for (const width of [320, 360, 375, 390, 430]) {
  test(`Existing Creator marketplace access has no legacy agreement gate at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    await login(context, "creator");
    await open(page, "/creator/promotions");
    await expect(page.getByRole("heading", { name: "My Promotions", exact: true })).toBeVisible();
    await expect(page.getByText("Before you continue", { exact: true })).toHaveCount(0);
    await expect(page.getByText(/Creator Agreement|Anti-Circumvention Rules/)).toHaveCount(0);
    await layout(page);
  });
}

test("Creator Promotion request opens normally without legacy agreement acceptance", async ({ page, context }) => {
  // Own this eligible Promotion: other BrowserHost tests may fill every slot
  // in their Promotions, and a focused run starts with no published Promotion.
  await login(context, "business");
  await page.goto("/business/campaigns/new?type=views-sales");
  await expect(page.getByRole("heading", { name: "Create Promotion" })).toBeVisible();
  const title = `Marketplace rules Promotion ${Date.now()}`;
  await page.getByLabel("Promotion title", { exact: true }).fill(title);
  await page.getByLabel("Application closes", { exact: true })
    .fill(new Date(Date.now() + 3 * 86400000).toISOString().slice(0, 16));
  await page.getByLabel("Content due", { exact: true })
    .fill(new Date(Date.now() + 7 * 86400000).toISOString().slice(0, 16));
  await page.getByLabel("Region", { exact: true }).fill("Addis Ababa");
  await page.getByRole("button", { name: "Add TikTok Creator slot" }).click();
  await page.getByLabel("Promotion budget", { exact: true }).fill("1000");
  await page.getByRole("button", { name: "Publish Promotion" }).click();
  await expect(page.getByRole("heading", { name: title, exact: true })).toBeVisible();

  await login(context, "creator");
  await open(page, "/creator/discover");
  const opportunity = page.locator(".creator-opportunity-card")
    .filter({ has: page.getByRole("heading", { name: title, exact: true }) });
  await opportunity.getByRole("link", { name: "Request to Join" }).click();
  await expect(page.getByRole("heading", { name: "Request to Join", exact: true })).toBeVisible();
  await expect(page.getByText("Before you continue", { exact: true })).toHaveCount(0);
  await expect(page.getByText(/Creator Agreement|Anti-Circumvention Rules/)).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Submit Request", exact: true })).toBeVisible();
});
