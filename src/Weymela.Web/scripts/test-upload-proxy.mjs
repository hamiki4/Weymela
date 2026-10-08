// Run only from the owned two-proxy regression; no live host is accepted.
import { readFileSync } from "node:fs";
import { chromium, expect } from "@playwright/test";
const [base, fixture] = process.argv.slice(2);
if (!/^http:\/\/127\.0\.0\.1:\d+$/.test(base ?? "")) throw new Error("Isolated loopback proxy required");
const control = JSON.parse(readFileSync(".artifacts/browser-host.json", "utf8"));
const browser = await chromium.launch();
try {
  const context = await browser.newContext({ baseURL: base, viewport: { width: 390, height: 844 } });
  const login = await context.request.post("/api/development/session", {
    headers: { "X-Weymela-Request": "1", Origin: base }, data: { alias: "business", accessKey: control.accessKey },
  });
  expect(login.status()).toBe(204);
  const page = await context.newPage();
  // Development identity normally hides ManualApproval UI; only select that UI mode.
  // The submission traverses both real proxies and the real frozen API, unmocked.
  await page.route("**/api/session", async route => {
    const response = await route.fetch();
    await route.fulfill({ response, json: { ...await response.json(), developmentMode: false } });
  });
  await page.route("**/api/account/security", route => route.fulfill({ json: { passwordEnrolled: true, phoneEnrolled: true } }));
  await page.route("**/api/business/deposit-method", route => route.fulfill({ json: { mode: "ManualApproval" } }));
  await page.goto("/business/wallet");
  await expect(page.getByText("Open Add Funds", { exact: true })).toHaveCount(0);
  await expect(page.getByLabel("Amount", { exact: true })).toBeVisible();
  await page.getByLabel("Amount", { exact: true }).fill("3000");
  await page.getByLabel("Payment receipt").setInputFiles(fixture);
  const response = page.waitForResponse(r => r.url().endsWith("/api/business/deposit-requests") && r.request().method() === "POST");
  await page.getByRole("button", { name: "Submit for Review" }).click();
  expect((await response).status()).toBe(503);
  await expect(page.getByText("Adding funds is temporarily paused. Your receipt was not submitted.")).toBeVisible();
  await expect(page.getByText("We could not complete that request. Please try again.")).toHaveCount(0);
  console.log("65,752-byte receipt through both proxies -> real frozen API -> paused UI PASS");
} finally { await browser.close(); }
