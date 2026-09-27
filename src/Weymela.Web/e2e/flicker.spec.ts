import { expect, test } from "@playwright/test";
import { login, open } from "./helpers";

const workspaces = [
  { alias: "customer", from: "/customer/offers", to: "/customer/discover" },
  { alias: "creator", from: "/creator", to: "/creator/discover" },
  { alias: "business", from: "/business", to: "/business/campaigns" },
  { alias: "cashier", from: "/checkout", to: "/notifications" },
  { alias: "operations-admin", from: "/admin/operations", to: "/admin/role-enrollments" },
  { alias: "admin", from: "/admin", to: "/admin/customers" },
] as const;

for (const { alias, from, to } of workspaces) {
  test(`${alias} navigation retains its shell and warm pages through browser history`, async ({ page, context }) => {
    await login(context, alias);
    await open(page, from);
    const fromHeading = await page.locator("main h1").first().innerText();
    const shell = page.locator(".app-shell");
    await shell.evaluate(element => { (element as HTMLElement & { flickerMarker?: string }).flickerMarker = "same-shell"; });
    const link = to === "/notifications"
      ? page.getByRole("link", { name: "Your notifications" })
      : ["customer", "creator", "business"].includes(alias)
        ? page.locator(".product-desktop-nav nav").locator(`a[href="${to}"]`)
        : page.getByRole("navigation", { name: "Main navigation" }).locator(`a[href="${to}"]`);
    await link.click();
    await expect(page).toHaveURL(new RegExp(`${to}$`));
    await expect(page.locator("main h1").first()).not.toHaveText(fromHeading);
    await expect(shell).toHaveJSProperty("flickerMarker", "same-shell");
    await expect(page.getByRole("status", { name: "Loading workspace" })).toHaveCount(0);

    await page.evaluate(() => {
      const tracked = window as Window & { flickerEvents?: string[] };
      tracked.flickerEvents = [];
      new MutationObserver(() => {
        const loading = document.querySelector(".sign-in, .pin-setup-page, .loading[aria-label='Loading workspace']");
        if (loading) {
          tracked.flickerEvents?.push(`${location.pathname}: ${loading.className}: ${loading.textContent?.trim().slice(0, 100)}`);
        }
      }).observe(document.body, { childList: true, subtree: true });
    });
    await page.goBack();
    await expect(page).toHaveURL(new RegExp(`${from}$`));
    await expect(page.locator("main h1").first()).toHaveText(fromHeading);
    await expect(shell).toHaveJSProperty("flickerMarker", "same-shell");
    await page.goForward();
    await expect(page).toHaveURL(new RegExp(`${to}$`));
    await expect(page.locator("main h1").first()).not.toHaveText(fromHeading);
    await expect(shell).toHaveJSProperty("flickerMarker", "same-shell");
    expect(await page.evaluate(() => (window as Window & { flickerEvents?: string[] }).flickerEvents)).toEqual([]);
  });
}

test("iPhone first visit keeps Business Home visible until Promotions data arrives", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(context, "business");
  await open(page, "/business");
  let release!: () => void;
  const held = new Promise<void>(resolve => { release = resolve; });
  await page.route("**/api/business/campaigns", async route => { await held; await route.continue(); });
  await page.evaluate(() => {
    const shell = document.querySelector(".app-shell") as HTMLElement & { pilotMarker?: string };
    shell.pilotMarker = "retained";
    (window as Window & { transitionFrames?: string[] }).transitionFrames = [];
    new MutationObserver(() => {
      if (document.querySelector("main .loading, .sign-in"))
        (window as Window & { transitionFrames?: string[] }).transitionFrames?.push("loading frame");
    }).observe(document.body, { childList: true, subtree: true });
  });
  await page.getByRole("navigation", { name: "Mobile navigation" }).getByRole("link", { name: "Promotions" }).click();
  await expect(page).toHaveURL(/\/business$/);
  await expect(page.getByRole("heading", { name: "Home" })).toBeVisible();
  release();
  await expect(page).toHaveURL(/\/business\/campaigns$/);
  await expect(page.getByRole("heading", { name: "Promotions", exact: true })).toBeVisible();
  expect(await page.evaluate(() => ({ marker: (document.querySelector(".app-shell") as HTMLElement & { pilotMarker?: string }).pilotMarker,
    frames: (window as Window & { transitionFrames?: string[] }).transitionFrames }))).toEqual({ marker: "retained", frames: [] });
});
