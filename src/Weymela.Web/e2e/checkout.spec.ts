import { test, expect } from "@playwright/test";
import { login, open, screenshot } from "./helpers";

test("real QR rejects wrong Business then confirms the same offer at its Business", async ({
  page,
  context,
}) => {
  await login(context, "customer");
  await open(page, "/customer/offers");
  await page
    .getByRole("link", { name: "Get Offer", exact: true })
    .first()
    .click();
  const issuing = page.waitForResponse(
    (r) => r.url().endsWith("/qr") && r.request().method() === "POST",
  );
  await page.getByRole("button", { name: "Get Offer", exact: true }).click();
  const qr = await (await issuing).json();
  await expect(
    page.getByRole("img", { name: "Offer QR for the cashier" }),
  ).toBeVisible();
  await screenshot(page, "checkout-customer-issued");
  await login(context, "other-cashier");
  await open(page, "/checkout");
  await page.getByText("Use a scanned code instead", { exact: true }).click();
  await page.getByLabel("Scanned QR code", { exact: true }).fill(qr.token);
  await page.getByRole("button", { name: "Resolve Offer" }).click();
  await expect(page.getByRole("alert")).toBeVisible();
  await expect(
    page.getByLabel("Purchase Amount", { exact: true }),
  ).toHaveCount(0);
  await login(context, "cashier");
  await open(page, "/checkout");
  await page.getByText("Use a scanned code instead", { exact: true }).click();
  await page.getByLabel("Scanned QR code", { exact: true }).fill(qr.token);
  await page.getByRole("button", { name: "Resolve Offer" }).click();
  await expect(
    page.getByLabel("Purchase Amount", { exact: true }),
  ).toBeVisible();
  await page.getByLabel("Purchase Amount", { exact: true }).fill("1000");
  await screenshot(page, "checkout-confirmation");
  await page.getByRole("button", { name: "Confirm Purchase" }).click();
  await expect(
    page.getByRole("heading", { name: "Purchase confirmed", exact: true }),
  ).toBeVisible();
  await screenshot(page, "checkout-confirmed");
  await login(context, "customer");
  const status = await (
    await context.request.get(`/api/customer/qr/${qr.id}`)
  ).json();
  expect(status.status).toBe("Used");
  await open(page, "/customer/history");
  await expect(page).toHaveURL(/\/customer\/transactions$/);
  await open(page, "/customer/transactions");
  await expect(page.locator("main")).toContainText("Abc Coffee");
  await expect(page.locator("main")).toContainText("Cashback earned");
  await open(page, "/customer/cashback");
  await expect(page.locator("main")).toContainText("Cashback");
  expect(await page.evaluate(() => Object.keys(localStorage))).toEqual([]);
});

test("camera scanner decodes the real issued QR and owner uses the same checkout", async ({
  page,
  context,
}) => {
  await login(context, "customer");
  await open(page, "/customer/offers");
  await page
    .getByRole("link", { name: "Get Offer", exact: true })
    .first()
    .click();
  await page.getByRole("button", { name: "Get Offer", exact: true }).click();
  const image = page.getByRole("img", { name: "Offer QR for the cashier" });
  await expect(image).toBeVisible();
  const src = await image.getAttribute("src");
  // Synthetic camera frames exercise the actual ZXing decoder. No API/finance response is mocked.
  await context.addInitScript(
    ({ src }) => {
      Object.defineProperty(navigator.mediaDevices, "getUserMedia", {
        value: async () => {
          const canvas = document.createElement("canvas");
          canvas.width = 640;
          canvas.height = 480;
          const ctx = canvas.getContext("2d")!;
          const image = new Image();
          image.src = src!;
          await image.decode();
          ctx.fillStyle = "white";
          ctx.fillRect(0, 0, 640, 480);
          ctx.drawImage(image, 160, 80, 320, 320);
          const stream = canvas.captureStream(10);
          const timer = setInterval(
            () => ctx.drawImage(image, 160, 80, 320, 320),
            100,
          );
          stream
            .getVideoTracks()[0]
            .addEventListener("ended", () => clearInterval(timer));
          return stream;
        },
      });
    },
    { src },
  );
  await login(context, "business");
  await open(page, "/checkout");
  await page.getByRole("button", { name: "Scan QR", exact: true }).click();
  await expect(
    page.getByLabel("Purchase Amount", { exact: true }),
  ).toBeVisible();
  await page.getByLabel("Purchase Amount", { exact: true }).fill("50");
  await page.getByRole("button", { name: "Confirm Purchase" }).click();
  await expect(
    page.getByRole("heading", { name: "Purchase confirmed", exact: true }),
  ).toBeVisible();
});

test("camera permission failure has a usable alternative", async ({
  page,
  context,
}) => {
  await context.addInitScript(() => Object.defineProperty(navigator.mediaDevices, "getUserMedia", { value: async () => { throw new DOMException("Denied", "NotAllowedError"); } }));
  await login(context, "cashier");
  await open(page, "/checkout");
  await page.getByRole("button", { name: "Scan QR", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText(
    "Camera permission was denied",
  );
  await page.getByText("Use a scanned code instead", { exact: true }).click();
  await expect(
    page.getByLabel("Scanned QR code", { exact: true }),
  ).toBeVisible();
});
