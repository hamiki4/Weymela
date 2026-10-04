import { expect, test } from "@playwright/test";
import { layout, login } from "./helpers";

for (const width of [320, 360, 375, 390, 430]) {
  test(`Business marketplace setup is available without legacy agreements at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    await login(context, "business");

    await page.goto("/business/campaigns/new");
    await expect(page).toHaveURL(/\/business\/campaigns\/new$/);
    await expect(page.getByRole("heading", { name: "Create Promotion", exact: true })).toBeVisible();
    await expect(page.getByText("Before you continue", { exact: true })).toHaveCount(0);
    await expect(page.getByText(/Business Terms|Anti-Circumvention Rules/)).toHaveCount(0);
    await layout(page);

    await page.goto("/business/ugc/new");
    await expect(page).toHaveURL(/\/business\/campaigns\/new\?type=ugc$/);
    await expect(page.getByRole("heading", { name: "Create Promotion", exact: true })).toBeVisible();
    await expect(page.getByText("Before you continue", { exact: true })).toHaveCount(0);
    await expect(page.getByText(/Business Terms|Anti-Circumvention Rules/)).toHaveCount(0);
    await layout(page);
  });
}
