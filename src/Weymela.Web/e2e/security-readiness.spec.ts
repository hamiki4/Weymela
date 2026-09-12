import { test, expect } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";

for (const width of [375, 390, 393, 430, 768, 1366, 1440, 1920])
  test(`operational error, offline and inbox layouts at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: width >= 1366 ? 900 : 844 });
    await login(context, "creator"); await open(page, "/notifications");
    await layout(page); await screenshot(page, `${width}-notifications`);
    await context.setOffline(true); await expect(page.getByRole("alert")).toContainText("Nothing is queued");
    await layout(page); await screenshot(page, `${width}-offline-workspace`); await context.setOffline(false);
    await login(context, "cashier");
    await context.addInitScript(() => Object.defineProperty(navigator.mediaDevices, "getUserMedia", { value: async () => { throw new DOMException("Denied", "NotAllowedError"); } }));
    await open(page, "/checkout"); await page.getByRole("button", { name: "Scan QR", exact: true }).click();
    await expect(page.getByRole("alert")).toContainText("Camera permission was denied");
    await layout(page); await screenshot(page, `${width}-camera-permission-denied`);
    await page.goto("/business"); await expect(page.getByRole("heading", { name: "This workspace isn’t available to your role" })).toBeVisible();
    await layout(page); await screenshot(page, `${width}-unauthorized`);
  });

test("durable in-app notification read state survives reload", async ({ page, context }) => {
  await login(context, "business"); await open(page, "/notifications");
  await expect(page.locator(".notification-item").first()).toBeVisible();
  const button=page.getByRole("button", {name:"Mark all read"}); if(await button.isEnabled()) await button.click();
  await expect(page.getByRole("heading", {name:"0 unread",exact:true})).toBeVisible(); await page.reload();
  await expect(page.getByRole("heading", {name:"0 unread",exact:true})).toBeVisible();
  await screenshot(page,"notifications-read-persisted");
});

test("service worker caches only safe offline assets and never financial responses", async ({ page, context }) => {
  await login(context, "business"); await open(page, "/business/wallet");
  await page.evaluate(async () => { await navigator.serviceWorker.ready; });
  await page.reload(); await expect(page.locator("main h1")).toBeVisible();
  const cached=await page.evaluate(async () => { const result:string[]=[]; for(const key of await caches.keys()) for(const request of await (await caches.open(key)).keys()) result.push(new URL(request.url).pathname); return result.sort(); });
  expect(cached).toEqual(["/offline.css","/offline.html"]);
  await context.setOffline(true); await page.goto("/business/wallet");
  await expect(page.getByRole("heading", {name:"You’re offline"})).toBeVisible();
  await expect(page.locator("body")).not.toContainText("10,000"); await screenshot(page,"pwa-safe-offline-document"); await context.setOffline(false);
});

test("production assets have safe headers and release-specific update identity", async ({ request }) => {
  const response=await request.get("/"); expect(response.headers()["permissions-policy"]).toContain("camera=(self)");
  expect(response.headers()["content-security-policy"]).toContain("frame-ancestors 'none'"); expect(response.headers()["cache-control"]).toBe("no-cache");
  const sw=await request.get("/sw.js"); expect(sw.headers()["cache-control"]).toBe("no-cache"); expect(await sw.text()).not.toContain("__BUILD_ID__");
  const manifest=await (await request.get("/manifest.webmanifest")).json();
  for(const size of [192,512]) { const icon=manifest.icons.find((x:{sizes:string})=>x.sizes===`${size}x${size}`); expect(icon).toBeTruthy(); expect((await request.get(icon.src)).status()).toBe(200); }
});
