import { test, expect } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";

const png = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=", "base64");

for (const width of [320, 360, 375, 390, 393, 430]) {
  test(`receipt upload and Admin review stay compact at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    await page.route("**/api/session", async route => {
      const response = await route.fetch();
      const session = await response.json();
      await route.fulfill({ response, json: { ...session, developmentMode: false } });
    });
    await page.route("**/api/account/security", route => route.fulfill({ json: { passwordEnrolled: true, phoneEnrolled: true } }));
    await page.route("**/api/business/deposit-method", route => route.fulfill({ json: { mode: "ManualApproval" } }));
    let submitted = false;
    await page.route("**/api/business/deposit-requests", async route => {
      if (route.request().method() === "POST") {
        expect(route.request().headers()["content-type"]).toContain("multipart/form-data");
        submitted = true;
        await route.fulfill({ json: { id: "receipt-one", amount: 10000, status: "Pending" } });
      } else await route.fulfill({ json: submitted ? [{ id: "receipt-one", amount: 10000, status: "Pending", submittedAtUtc: "2026-09-28T12:00:00Z" }] : [] });
    });
    await login(context, "business");
    await open(page, "/business/wallet");
    await expect(page.getByLabel("Payment receipt")).toBeAttached();
    await expect(page.getByText("Upload your payment receipt for Admin review.")).toHaveCount(0);
    await expect(page.getByLabel("Payment reference")).toHaveCount(0);
    await page.getByLabel("Amount", { exact: true }).fill("10000");
    await page.getByLabel("Payment receipt").setInputFiles({ name: "receipt.png", mimeType: "image/png", buffer: png });
    await expect(page.getByAltText("Selected payment receipt")).toBeVisible();
    await expect.poll(() => page.getByAltText("Selected payment receipt").evaluate(image => (image as HTMLImageElement).naturalWidth)).toBeGreaterThan(0);
    await expect(page.getByRole("button", { name: "Replace" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Remove" })).toBeVisible();
    const thumbnail = await page.locator(".receipt-thumbnail").boundingBox();
    expect(thumbnail!.width).toBeLessThanOrEqual(74);
    expect(thumbnail!.height).toBeLessThanOrEqual(74);
    await layout(page);
    if (width === 390 || width === 393) await screenshot(page, `${width}-business-wallet-receipt`);
    await page.getByRole("button", { name: "Submit for Review" }).click();
    await expect(page.getByText("Your payment is waiting for approval.")).toBeVisible();
    await expect(page.getByText("Under review").first()).toBeVisible();
    await layout(page);

    const pending = ["first", "second"].map(id => ({ id, businessId: "biz", business: "Abc Coffee", amount: 10000,
      status: "Pending", hasReceipt: true, submittedAtUtc: "2026-09-28T12:00:00Z", version: 0 }));
    await page.route("**/api/admin/deposit-requests", route => route.fulfill({ json: pending }));
    await page.route("**/api/admin/deposit-requests/*/receipt", route => route.fulfill({ status: 200, contentType: "image/png", body: png,
      headers: { "Cache-Control": "private,no-store", "Content-Disposition": "inline; filename=receipt.png" } }));
    const reviews: boolean[] = [];
    await page.route("**/api/admin/deposit-requests/*/review", async route => {
      const body = route.request().postDataJSON() as { approve: boolean };
      reviews.push(body.approve);
      const id = route.request().url().split("/").at(-2);
      const index = pending.findIndex(row => row.id === id);
      if (index >= 0) pending.splice(index, 1);
      await route.fulfill({ json: { id } });
    });
    await login(context, "admin");
    await open(page, "/admin/wallets");
    await expect(page.getByRole("heading", { name: "Deposit review · 2 pending" })).toBeVisible();
    await layout(page);
    await page.getByRole("button", { name: "Review", exact: true }).first().click();
    const dialog = page.getByRole("dialog", { name: /Review deposit/ });
    await expect(dialog.getByAltText("Payment receipt for verification")).toBeVisible();
    await expect.poll(() => dialog.getByAltText("Payment receipt for verification").evaluate(image => (image as HTMLImageElement).naturalWidth)).toBeGreaterThan(0);
    await layout(page);
    await dialog.getByLabel("Confirmation reference or reason code").fill("BANK-10000");
    await dialog.getByRole("button", { name: "Approve" }).click();
    await expect(page.getByText("Deposit approved. The wallet balance has been credited.")).toBeVisible();
    await page.getByRole("button", { name: "Review", exact: true }).first().click();
    const reject = page.getByRole("dialog", { name: /Review deposit/ });
    await reject.getByLabel("Confirmation reference or reason code").fill("REJECT-UNMATCHED");
    await reject.getByRole("button", { name: "Reject" }).click();
    await expect(page.getByText("Deposit rejected. The wallet balance has not changed.")).toBeVisible();
    expect(reviews).toEqual([true, false]);
    await layout(page);
  });
}

test("frozen receipt submission shows the typed pause without claiming a deposit", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.route("**/api/session", async route => {
    const response = await route.fetch();
    const session = await response.json();
    await route.fulfill({ response, json: { ...session, developmentMode: false } });
  });
  await page.route("**/api/account/security", route => route.fulfill({ json: { passwordEnrolled: true, phoneEnrolled: true } }));
  await page.route("**/api/business/deposit-method", route => route.fulfill({ json: { mode: "ManualApproval" } }));
  let posts = 0;
  await page.route("**/api/business/deposit-requests", route => {
    if (route.request().method() === "POST") {
      posts++;
      return route.fulfill({ status: 503, json: { code: "FinancialWritesPaused" } });
    }
    return route.fulfill({ json: [] });
  });
  await login(context, "business");
  await open(page, "/business/wallet");
  await page.getByLabel("Amount", { exact: true }).fill("100");
  await page.getByLabel("Payment receipt").setInputFiles({ name: "receipt.png", mimeType: "image/png", buffer: png });
  await page.getByRole("button", { name: "Submit for Review" }).click();
  await expect(page.getByText("Adding funds is temporarily paused. Your receipt was not submitted.")).toBeVisible();
  await expect(page.getByText("Your payment is waiting for approval.")).toHaveCount(0);
  expect(posts).toBe(1);
});


test("receipt larger than 4 MiB is rejected locally without a POST", async ({ page, context }) => {
  await page.route("**/api/session", async route => {
    const response = await route.fetch();
    await route.fulfill({ response, json: { ...await response.json(), developmentMode: false } });
  });
  await page.route("**/api/account/security", route => route.fulfill({ json: { passwordEnrolled: true, phoneEnrolled: true } }));
  await page.route("**/api/business/deposit-method", route => route.fulfill({ json: { mode: "ManualApproval" } }));
  let posts = 0;
  await page.route("**/api/business/deposit-requests", route => {
    if (route.request().method() === "POST") posts++;
    return route.fulfill({ json: [] });
  });
  await login(context, "business"); await open(page, "/business/wallet");
  await page.getByLabel("Amount", { exact: true }).fill("3000");
  await page.getByLabel("Payment receipt").setInputFiles({ name: "large.png", mimeType: "image/png", buffer: Buffer.alloc(4 * 1024 * 1024 + 1) });
  await expect(page.getByText("Receipt must be 4 MB or smaller.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Submit for Review" })).toBeDisabled();
  expect(posts).toBe(0);
});
