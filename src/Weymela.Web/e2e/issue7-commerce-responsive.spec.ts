import { expect, test } from "@playwright/test";
import { layout, login, open } from "./helpers";

for (const width of [320, 360, 375, 390, 393, 430]) {
  test(`Issue 7 commerce pages remain compact at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120000);
    await page.setViewportSize({ width, height: 900 });

    await login(context, "business");
    for (const path of ["/business/pricing", "/business/transactions", "/checkout"]) {
      await open(page, path);
      await layout(page);
    }
    await page.getByRole("button", { name: "Enter Manually", exact: true }).click();
    await expect(page.getByLabel("Creator ID", { exact: true })).toBeVisible();
    await layout(page);

    await login(context, "creator");
    for (const path of ["/creator/discover", "/creator/pricing", "/creator/earnings"]) {
      await open(page, path);
      await layout(page);
    }
    await expect(page.getByRole("heading", { name: "Earnings", exact: true })).toBeVisible();
    await open(page, "/creator/discover");
    const firstCard = page.locator(".creator-opportunity-card").first();
    if (await firstCard.count()) {
      const separated = await firstCard.evaluate((element) => {
        const style = getComputedStyle(element);
        return parseFloat(style.borderTopWidth) > 0 || style.boxShadow !== "none";
      });
      expect(separated).toBeTruthy();
    }
    await page.getByRole("tab", { name: "UGC" }).click();
    await layout(page);

    await login(context, "customer");
    for (const path of ["/customer/offers", "/customer/transactions"]) {
      await open(page, path);
      await layout(page);
    }
    await open(page, "/customer/offers");
    const offerLink = page.getByRole("link", { name: "Get Offer", exact: true }).first();
    if (await offerLink.count()) {
      await offerLink.click();
      await layout(page);
    }

    await login(context, "cashier");
    for (const path of ["/checkout", "/checkout/transactions"]) {
      await open(page, path);
      await layout(page);
    }
    await login(context, "admin");
    await open(page, "/admin/reports");
    await layout(page);
  });
}
