import { expect, test } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";

test("Creator social accounts are read from the Creator-only server endpoint", async ({ context }) => {
  await login(context, "creator");
  const creatorResponse = await context.request.get("/api/creator/social-accounts", { headers: { "X-Weymela-Request": "1" } });
  expect(creatorResponse.status()).toBe(200);
  expect(Array.isArray(await creatorResponse.json())).toBe(true);
  await login(context, "customer");
  const customerResponse = await context.request.get("/api/creator/social-accounts", { headers: { "X-Weymela-Request": "1" } });
  expect(customerResponse.status()).toBe(403);
});

for (const width of [320, 360, 375, 390, 430]) {
  test(`Pilot mobile navigation and layout at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    for (const [role, home, destination] of [
      ["business", "/business", "/business/campaigns"],
      ["creator", "/creator", "/creator/earnings"],
      ["customer", "/customer/offers", "/customer/discover"],
    ] as const) {
      await login(context, role);
      await open(page, home);
      await layout(page);
      const nav = page.getByRole("navigation", { name: "Mobile navigation" });
      await expect(nav).toBeVisible();
      await nav.locator(`a[href="${destination}"]`).click();
      await expect(page).toHaveURL(new RegExp(`${destination}$`));
      await expect(page.getByRole("status", { name: "Loading workspace" })).toHaveCount(0);
      await layout(page);
    }
  });
}

test("captures the nine Pilot correction views", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  for (const [role, views] of [
    ["business", [["/business", "business-home"], ["/business/campaigns", "business-promotions"]]],
    ["creator", [["/creator", "creator-home"], ["/creator/earnings", "creator-earnings"], ["/creator/profile", "creator-profile-social-accounts"]]],
    ["customer", [["/customer/offers", "customer-home"]]],
  ] as const) {
    await login(context, role);
    for (const [path, name] of views) {
      await open(page, path);
      await layout(page);
      await screenshot(page, `pilot-correction-${name}`);
    }
    await open(page, views[0][0]);
    await page.getByRole("navigation", { name: "Mobile navigation" }).getByRole("button", { name: "Profile" }).click();
    await expect(page.getByRole("dialog", { name: "Account menu" })).toBeVisible();
    await screenshot(page, `pilot-correction-${role}-account-menu`);
  }
});
