import { test, expect, type BrowserContext, type Page } from "@playwright/test";
import { createPublishedUgc, installQrCamera, layout, login, open, scanQr } from "./helpers";

const requestHeaders = { "X-Weymela-Request": "1" };
const manualCustomerPhone = "0911111111";

async function creatorNumber(context: BrowserContext, alias: "creator" | "other-creator") {
  await login(context, alias);
  return String((await apiJson<{ creatorId: number }>(context, "/profile")).creatorId);
}

async function apiPost(
  context: BrowserContext,
  path: string,
  body: unknown,
  key = crypto.randomUUID(),
) {
  const response = await context.request.post(`/api${path}`, {
    headers: { ...requestHeaders, "Idempotency-Key": key },
    data: body,
  });
  return response;
}

async function apiJson<T>(context: BrowserContext, path: string): Promise<T> {
  const response = await context.request.get(`/api${path}`, {
    headers: requestHeaders,
  });
  expect(response.ok()).toBeTruthy();
  return (await response.json()) as T;
}

async function createCashier(
  context: BrowserContext,
  name: string,
  phone: string,
) {
  const response = await apiPost(context, "/business/cashiers", { name, phone });
  expect(response.ok()).toBeTruthy();
  return (await response.json()) as {
    cashier: { id: string; name: string; status: string };
    activationCode: string;
  };
}

async function assertRoleColor(
  context: BrowserContext,
  page: Page,
  alias: string,
  path: string,
  role: string,
  accent: string,
) {
  await login(context, alias);
  await open(page, path);
  const shell = page.locator(`.app-shell.role-${role}`);
  await expect(shell).toBeVisible();
  await expect
    .poll(() => shell.evaluate((element) => getComputedStyle(element).getPropertyValue("--role-accent").trim()))
    .toBe(accent);
}

test("Business presentation keeps separate checkout and Cashier Management destinations", async ({
  page,
  context,
}) => {
  await login(context, "business");
  await open(page, "/business");
  const shell = page.locator(".app-shell.role-business");
  await expect(shell).toBeVisible();
  await expect(page.locator("main")).toHaveCSS("color", "rgb(17, 24, 39)");
  await expect(page.getByRole("link", { name: "Checkout / Scan QR", exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "Cashier Management", exact: true })).toBeVisible();
  await page.getByRole("link", { name: "Checkout / Scan QR", exact: true }).click();
  await expect(page).toHaveURL(/\/checkout$/);
  await page.goto("/business");
  await page.getByRole("link", { name: "Cashier Management", exact: true }).click();
  await expect(page).toHaveURL(/\/business\/cashiers$/);
});

test("Customer, Creator, and Business retain their distinct role colors", async ({
  page,
  context,
}) => {
  await assertRoleColor(context, page, "customer", "/customer/offers", "customer", "#16632e");
  await assertRoleColor(context, page, "creator", "/creator", "creator", "#6034c4");
  await assertRoleColor(context, page, "business", "/business", "business", "#1d4fc1");
});

test("Business can create, hide activation codes from lists, and manage Cashier state", async ({
  page,
  context,
  browser,
}) => {
  await login(context, "business");
  await open(page, "/business/cashiers");
  await expect(page.getByRole("button", { name: "Add Cashier", exact: true })).toBeVisible();
  await page.getByLabel("Cashier name", { exact: true }).fill("Sara Test");
  await page.getByLabel("Cashier phone number", { exact: true }).fill("0912345678");
  await page.getByRole("button", { name: "Add Cashier", exact: true }).click();
  const notice = page.getByRole("status");
  await expect(notice).toContainText("Cashier created");
  const code = (await notice.innerText()).match(/\b\d{6}\b/)?.[0];
  expect(code).toMatch(/^\d{6}$/);
  await expect(page.getByText("Pending Activation", { exact: true })).toBeVisible();
  await expect(page.getByText("0912", { exact: false })).toHaveCount(0);
  await page.reload();
  await expect(page.getByText(code!, { exact: true })).toHaveCount(0);
  const rows = await apiJson<{ id: string; maskedPhone: string; status: string }[]>(
    context,
    "/business/cashiers",
  );
  const created = rows.find((row) => row.status === "Pending Activation");
  expect(created).toBeDefined();
  expect(created!.maskedPhone).not.toContain("0912345678");
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(page.getByText("Disabled", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Enable", exact: true }).click();
  await expect(page.getByText("Pending Activation", { exact: true })).toBeVisible();

  const other = await browser.newContext({ baseURL: new URL(page.url()).origin });
  try {
    await login(other, "other-business");
    const otherRows = await apiJson<{ id: string }[]>(other, "/business/cashiers");
    expect(otherRows.some((row) => row.id === created!.id)).toBeFalsy();
    const forbidden = await apiPost(other, `/business/cashiers/${created!.id}/disable`, undefined);
    expect(forbidden.ok()).toBeFalsy();
  } finally {
    await other.close();
  }
});

test("Cashier activation uses the real activation, password, and PIN flow", async ({
  page,
  context,
  browser,
}) => {
  await login(context, "business");
  const created = await createCashier(context, "Activation Test", "0912345679");
  const activation = await browser.newContext();
  const activationPage = await activation.newPage();
  try {
    await activationPage.goto("/cashier/activate");
    await activationPage.getByLabel("Phone number", { exact: true }).fill("0912345679");
    await activationPage.getByLabel("Activation code", { exact: true }).fill("000000");
    await activationPage.getByRole("button", { name: "Activate Cashier access", exact: true }).click();
    await expect(activationPage.getByRole("alert")).toContainText("invalid or expired");

    const activated = await apiPost(activation, "/auth/cashier/activate", {
      phone: "0912345679",
      activationCode: created.activationCode,
    });
    expect(activated.ok()).toBeTruthy();
    const activationResult = (await activated.json()) as { token: { customToken: string } };
    const session = await activation.request.post("/api/auth/firebase/session", {
      headers: requestHeaders,
      data: { idToken: activationResult.token.customToken },
    });
    expect(session.status()).toBe(204);

    await activationPage.goto("/security-setup");
    await expect(activationPage.getByRole("heading", { name: "Secure your account" })).toBeVisible();
    await activationPage.getByLabel("Password", { exact: true }).fill("Cashier browser passphrase");
    await activationPage.getByLabel("Confirm password", { exact: true }).fill("Cashier browser passphrase");
    await activationPage.getByRole("button", { name: "Continue", exact: true }).click();
    await expect(activationPage.getByRole("heading", { name: "Create your PIN" })).toBeVisible();
    await activationPage.getByLabel("Create PIN, digit 1 of 5").fill("1");
    await activationPage.getByLabel("Create PIN, digit 2 of 5").fill("2");
    await activationPage.getByLabel("Create PIN, digit 3 of 5").fill("3");
    await activationPage.getByLabel("Create PIN, digit 4 of 5").fill("4");
    await activationPage.getByLabel("Create PIN, digit 5 of 5").fill("5");
    await activationPage.getByLabel("Confirm PIN, digit 1 of 5").fill("1");
    await activationPage.getByLabel("Confirm PIN, digit 2 of 5").fill("2");
    await activationPage.getByLabel("Confirm PIN, digit 3 of 5").fill("3");
    await activationPage.getByLabel("Confirm PIN, digit 4 of 5").fill("4");
    await activationPage.getByLabel("Confirm PIN, digit 5 of 5").fill("5");
    await activationPage.getByRole("button", { name: "Continue", exact: true }).click();
    await expect(activationPage).toHaveURL(/\/checkout$/);
    await expect(activationPage.getByText("Cashier checkout", { exact: true })).toBeVisible();
    await expect(activationPage.getByRole("button", { name: "Scan QR", exact: true })).toBeVisible();
    await expect(activationPage.getByText("Wallet", { exact: true })).toHaveCount(0);
    await expect(activationPage.getByText("Promotions", { exact: true })).toHaveCount(0);
    await expect(activationPage.getByText("Cashier Management", { exact: true })).toHaveCount(0);

    await open(page, "/business/cashiers");
    const activeRow = page.locator("article.cashier-row").filter({ hasText: "Activation Test" });
    await activeRow.getByRole("button", { name: "Disable", exact: true }).click();
    await expect(activeRow).toContainText("Disabled");
    await activeRow.getByRole("button", { name: "Enable", exact: true }).click();
    await expect(activeRow).toContainText("Active");

    const repeated = await apiPost(activation, "/auth/cashier/activate", {
      phone: "0912345679",
      activationCode: created.activationCode,
    });
    expect(repeated.ok()).toBeFalsy();
  } finally {
    await activation.close();
  }
});

test("Checkout shows stable QR outcomes without exposing technical errors", async ({ page, context }) => {
  await login(context, "business");
  await installQrCamera(context, "not-a-valid-qr");
  await open(page, "/checkout");
  await scanQr(page);
  await expect(page.getByRole("alert")).toHaveText("Invalid QR code");

  await login(context, "customer");
  await open(page, "/customer/offers");
  const promotionCard = page.locator("article.customer-promotion-card").filter({ hasText: "A little coffee. A great story." });
  await promotionCard.getByRole("link", { name: "Get Offer QR", exact: true }).click();
  const issuing = page.waitForResponse((response) => response.url().endsWith("/qr") && response.request().method() === "POST");
  await page.getByRole("button", { name: "Get Offer QR", exact: true }).click();
  const qr = await (await issuing).json() as { token: string };
  await login(context, "business");
  await installQrCamera(context, qr.token);
  await open(page, "/checkout");
  await scanQr(page);
  await expect(page.getByLabel("Total Purchase Amount", { exact: true })).toBeVisible();
  await page.getByLabel("Total Purchase Amount", { exact: true }).fill("25");
  await page.getByRole("button", { name: "Submit", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Payment recorded", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Scan Another QR", exact: true }).click();
  await scanQr(page);
  await expect(page.getByRole("alert")).toHaveText("QR code already used");
});

test("Manual View + Sale rejects empty matches and records an eligible checkout once", async ({ page, context }) => {
  await login(context, "business");
  await open(page, "/checkout");
  await page.getByRole("button", { name: "Enter Manually", exact: true }).click();
  await page.getByLabel("Creator ID", { exact: true }).fill("99999999");
  await page.getByLabel("Customer phone", { exact: true }).fill(manualCustomerPhone);
  await page.getByRole("button", { name: "Find eligible offers", exact: true }).click();
  await expect(page.getByRole("alert")).toHaveText("Offer is no longer available");

  const number = await creatorNumber(context, "creator");
  await login(context, "business");
  await page.getByLabel("Creator ID", { exact: true }).fill(number);
  await page.getByRole("button", { name: "Find eligible offers", exact: true }).click();
  const viewAndSale = page.locator("label.choice-row").filter({ hasText: "A little coffee. A great story." });
  await expect(viewAndSale.getByRole("radio")).toHaveCount(1);
  await viewAndSale.getByRole("radio").check();
  await page.getByLabel("Total Purchase Amount", { exact: true }).fill("25");
  await page.getByRole("button", { name: "Submit", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Payment recorded", exact: true })).toBeVisible();
  await expect(page.locator("p").filter({ hasText: "Purchase total:" })).toHaveText("Purchase total: 25");
});

test("Manual UGC requires the intended offer when multiple eligible offers exist", async ({ page, context }) => {
  const otherNumber = await creatorNumber(context, "other-creator");
  const singleTitle = `Browser UGC Single ${Date.now()}`;
  await createPublishedUgc(context, singleTitle, "other-creator");
  await login(context, "business");
  await open(page, "/checkout");
  await page.getByRole("button", { name: "Enter Manually", exact: true }).click();
  await page.getByLabel("Creator ID", { exact: true }).fill(otherNumber);
  await page.getByLabel("Customer phone", { exact: true }).fill(manualCustomerPhone);
  await page.getByRole("button", { name: "Find eligible offers", exact: true }).click();
  const singleOffer = page.locator("label.choice-row").filter({ hasText: singleTitle });
  await expect(singleOffer.getByRole("radio")).toHaveCount(1);
  await expect(singleOffer).toBeVisible();

  const firstTitle = `Browser UGC A ${Date.now()}`;
  const secondTitle = `Browser UGC B ${Date.now()}`;
  const first = await createPublishedUgc(context, firstTitle, "creator");
  const second = await createPublishedUgc(context, secondTitle, "creator");
  const number = await creatorNumber(context, "creator");
  await login(context, "business");
  await open(page, "/checkout");
  await page.getByRole("button", { name: "Enter Manually", exact: true }).click();
  await page.getByLabel("Creator ID", { exact: true }).fill(number);
  await page.getByLabel("Customer phone", { exact: true }).fill(manualCustomerPhone);
  await page.getByRole("button", { name: "Find eligible offers", exact: true }).click();
  await expect(page.getByText(secondTitle, { exact: false })).toBeVisible();
  expect(await page.getByRole("radio").count()).toBeGreaterThanOrEqual(2);
  const beforeFirst = await apiJson<{ opportunity: { customerOfferRemaining?: number } }>(context, `/business/ugc/${first}`);
  const beforeSecond = await apiJson<{ opportunity: { customerOfferRemaining?: number } }>(context, `/business/ugc/${second}`);
  const selectedOffer = page.locator("label.choice-row").filter({ hasText: secondTitle }).getByRole("radio");
  await selectedOffer.check();
  const selectedOfferId = await selectedOffer.inputValue();

  expect(selectedOfferId).toBeTruthy();
  await page.getByLabel("Total Purchase Amount", { exact: true }).fill("100");
  await page.getByRole("button", { name: "Submit", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Payment recorded", exact: true })).toBeVisible();
  const afterFirst = await apiJson<{ opportunity: { customerOfferRemaining?: number } }>(context, `/business/ugc/${first}`);
  const afterSecond = await apiJson<{ opportunity: { customerOfferRemaining?: number } }>(context, `/business/ugc/${second}`);
  expect(afterFirst.opportunity.customerOfferRemaining).toBe(beforeFirst.opportunity.customerOfferRemaining);
  expect(afterSecond.opportunity.customerOfferRemaining).toBeLessThan(beforeSecond.opportunity.customerOfferRemaining!);
});

test("Cashier and Business recent checkout views stay scoped and safe", async ({ page, context }) => {
  await login(context, "cashier");
  await open(page, "/checkout");
  await expect(page.getByRole("heading", { name: "Recent Transactions", exact: true })).toBeVisible();
  await expect(page.locator("main")).toContainText("A little coffee. A great story.");
  await expect(page.getByRole("link", { name: "Settings", exact: true }).first()).toBeVisible();
  await expect(page.getByText("Platform Fee", { exact: true })).toHaveCount(0);
  await login(context, "other-cashier");
  await open(page, "/checkout");
  await expect(page.locator("main")).not.toContainText("A little coffee. A great story.");
  await login(context, "business");
  await open(page, "/checkout");
  await expect(page.locator("main")).toContainText("A little coffee. A great story.");
  await expect(page.locator("main")).toContainText("Abc Checkout");
});

test("Expired and no-longer-eligible QR outcomes are deterministic BrowserHost fixtures", async ({ page, context }) => {
  await login(context, "customer");
  await open(page, "/customer/offers");
  await page.locator("article.customer-promotion-card").filter({ hasText: "A little coffee. A great story." })
    .getByRole("link", { name: "Get Offer QR", exact: true }).click();
  const issuing = page.waitForResponse((response) => response.url().endsWith("/qr") && response.request().method() === "POST");
  await page.getByRole("button", { name: "Get Offer QR", exact: true }).click();
  const issued = await (await issuing).json() as { token: string };
  const expireResponse = await context.request.post("/__test/qr/expired", {
    headers: { ...requestHeaders, "Idempotency-Key": crypto.randomUUID() },
    data: { token: issued.token },
  });
  expect(expireResponse.ok()).toBeTruthy();
  const expired = (await expireResponse.json()) as { token: string };
  await login(context, "business");
  await installQrCamera(context, expired.token);
  await open(page, "/checkout");
  await scanQr(page);
  await expect(page.getByRole("alert")).toHaveText("QR code expired. Ask the Customer to generate a new one.");

  await login(context, "customer");
  await open(page, "/customer/offers");
  await page.getByRole("link", { name: "Get Offer QR", exact: true }).first().click();
  const secondIssuing = page.waitForResponse((response) => response.url().endsWith("/qr") && response.request().method() === "POST");
  await page.getByRole("button", { name: "Get Offer QR", exact: true }).click();
  const ineligible = await (await secondIssuing).json() as { token: string };
  const ineligibleResponse = await context.request.post("/__test/qr/not-eligible", {
    headers: { ...requestHeaders, "Idempotency-Key": crypto.randomUUID() },
    data: { token: ineligible.token },
  });
  expect(ineligibleResponse.status()).toBe(204);
  await login(context, "business");
  await installQrCamera(context, ineligible.token);
  await open(page, "/checkout");
  await scanQr(page);
  await expect(page.getByRole("alert")).toHaveText("Offer is no longer available.");
});

test("Business and Cashier checkout layouts stay usable at required widths", async ({ page, context }) => {
  for (const width of [320, 360, 375, 390, 430]) {
    await page.setViewportSize({ width, height: 900 });
    await login(context, "business");
    await open(page, "/business");
    await layout(page);
    await expect(page.getByRole("link", { name: "Checkout / Scan QR", exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: "Cashier Management", exact: true })).toBeVisible();
    await open(page, "/business/transactions");
    await layout(page);
    await login(context, "cashier");
    await open(page, "/checkout");
    await layout(page);
    await expect(page.getByRole("button", { name: "Scan QR", exact: true })).toBeVisible();
  }
});
