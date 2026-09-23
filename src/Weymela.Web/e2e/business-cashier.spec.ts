import { test, expect, type BrowserContext, type Page } from "@playwright/test";
import { layout, login, open } from "./helpers";

const requestHeaders = { "X-Weymela-Request": "1" };
const manualCustomerPhone = "0911111111";

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

async function createPublishedUgc(
  context: BrowserContext,
  title: string,
  creatorAlias: "creator" | "other-creator",
) {
  await login(context, "admin");
  const settings = await apiJson<{
    current: {
      viewOnly: unknown;
      viewPlusCommission: unknown;
      creatorCommissionPercent: number;
      customerCashbackPercent: number;
      platformPercent: number;
      creatorThreshold: number;
      customerThreshold: number;
      effectiveFromUtc: string | null;
      promotionLiveDurationDays: number;
      ugc: {
        minimumCreatorPayment: number;
        platformFeePercent: number;
        minimumUgcBudget: number | null;
        customerOfferPlatformSalePercent: number | null;
      } | null;
    };
    version: number;
  }>(context, "/admin/financial-settings");
  expect(settings.current.ugc).not.toBeNull();
  if (settings.current.ugc?.customerOfferPlatformSalePercent == null) {
    const updated = await apiPost(context, "/admin/financial-settings", {
      settings: {
        ...settings.current,
        effectiveFromUtc: null,
        ugc: {
          ...settings.current.ugc!,
          customerOfferPlatformSalePercent: 3,
        },
      },
      expectedVersion: settings.version,
    });
    if (!updated.ok()) {
      throw new Error(`UGC pricing fixture update failed: ${updated.status()} ${await updated.text()}`);
    }
  }
  await login(context, "business");
  const due = new Date(Date.now() + 48 * 60 * 60 * 1000).toISOString();
  const created = await apiPost(context, "/business/ugc", {
    title,
    slogan: null,
    contentType: "Video",
    instructions: "Deliver one short product video.",
    resources: [],
    location: "Addis Ababa",
    dueDateUtc: due,
    productProvided: false,
    creatorMustPurchase: false,
    usageRights: null,
    creatorPayment: 200,
    creatorsNeeded: 1,
    platformRequirements: [],
    customerOfferEnabled: true,
    customerDiscountPercent: 2,
    customerOfferFundedAllocation: 500,
    customerFacingSlogan: `${title} Customer Offer`,
    customerOfferStartsAtUtc: new Date(Date.now() - 60_000).toISOString(),
    customerOfferEndsAtUtc: due,
  });
  if (!created.ok()) {
    throw new Error(`UGC fixture creation failed: ${created.status()} ${await created.text()}`);
  }
  const opportunityId = ((await created.json()) as { id: string }).id;
  const detail = await apiJson<{ version: number }>(
    context,
    `/business/ugc/${opportunityId}`,
  );
  expect(
    (await apiPost(context, `/business/ugc/${opportunityId}/publish`, {
      version: detail.version,
    })).ok(),
  ).toBeTruthy();

  await login(context, creatorAlias);
  expect((await apiPost(context, `/creator/ugc/${opportunityId}/request`, undefined)).ok()).toBeTruthy();
  await login(context, "business");
  const requested = await apiJson<{
    requests: { id: string; status: string }[];
  }>(context, `/business/ugc/${opportunityId}`);
  const request = requested.requests.find((item) => item.status === "Pending");
  expect(request).toBeDefined();
  expect(
    (await apiPost(context, `/business/ugc/requests/${request!.id}/approve`, {
      reason: null,
    })).ok(),
  ).toBeTruthy();
  return opportunityId;
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
  await expect(page.locator("main")).toHaveCSS("color", /rgb\(36, 59, 48\)|rgb\(0, 0, 0\)/);
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
    const activeCard = page.locator("article.workspace-card").filter({ hasText: "Activation Test" });
    await activeCard.getByRole("button", { name: "Disable", exact: true }).click();
    await expect(activeCard).toContainText("Disabled");
    await activeCard.getByRole("button", { name: "Enable", exact: true }).click();
    await expect(activeCard).toContainText("Active");

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
  await open(page, "/checkout");
  await page.getByText("Enter an opaque QR code", { exact: true }).click();
  await page.getByLabel("QR code", { exact: true }).fill("not-a-valid-qr");
  await page.getByRole("button", { name: "Resolve QR", exact: true }).click();
  await expect(page.getByRole("alert")).toHaveText("Invalid QR code");

  await login(context, "customer");
  await open(page, "/customer/offers");
  const promotionCard = page.locator("article.customer-promotion-card").filter({ hasText: "A little coffee. A great story." });
  await promotionCard.getByRole("link", { name: "Get Offer", exact: true }).click();
  const issuing = page.waitForResponse((response) => response.url().endsWith("/qr") && response.request().method() === "POST");
  await page.getByRole("button", { name: "Get Offer", exact: true }).click();
  const qr = await (await issuing).json() as { token: string };
  await login(context, "business");
  await open(page, "/checkout");
  await page.getByText("Enter an opaque QR code", { exact: true }).click();
  await page.getByLabel("QR code", { exact: true }).fill(qr.token);
  await page.getByRole("button", { name: "Resolve QR", exact: true }).click();
  await expect(page.getByLabel("Total Purchase Amount", { exact: true })).toBeVisible();
  await page.getByLabel("Total Purchase Amount", { exact: true }).fill("25");
  await page.getByRole("button", { name: "Submit", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Payment recorded", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Scan Another QR", exact: true }).click();
  await page.getByText("Enter an opaque QR code", { exact: true }).click();
  await page.getByLabel("QR code", { exact: true }).fill(qr.token);
  await page.getByRole("button", { name: "Resolve QR", exact: true }).click();
  await expect(page.getByRole("alert")).toHaveText("QR code already used");
});

test("Manual View + Sale rejects empty matches and records an eligible checkout once", async ({ page, context }) => {
  await login(context, "business");
  await open(page, "/checkout");
  await page.getByRole("button", { name: "Enter Manually", exact: true }).click();
  await page.getByLabel("Creator ID", { exact: true }).fill("CR-999");
  await page.getByLabel("Customer phone", { exact: true }).fill(manualCustomerPhone);
  await page.getByRole("button", { name: "Find eligible offers", exact: true }).click();
  await expect(page.getByRole("alert")).toHaveText("Offer is no longer available");

  await page.getByLabel("Creator ID", { exact: true }).fill("CR-100");
  await page.getByRole("button", { name: "Find eligible offers", exact: true }).click();
  await expect(page.getByRole("radio")).toHaveCount(1);
  await expect(page.getByRole("radio")).toBeChecked();
  await page.getByLabel("Total Purchase Amount", { exact: true }).fill("25");
  await page.getByRole("button", { name: "Submit", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Payment recorded", exact: true })).toBeVisible();
  await expect(page.locator("p").filter({ hasText: "Purchase total:" })).toHaveText("Purchase total: 25");
});

test("Manual UGC requires the intended offer when multiple eligible offers exist", async ({ page, context }) => {
  await createPublishedUgc(context, `Browser UGC Single ${Date.now()}`, "other-creator");
  await login(context, "business");
  await open(page, "/checkout");
  await page.getByRole("button", { name: "Enter Manually", exact: true }).click();
  await page.getByLabel("Creator ID", { exact: true }).fill("CR-200");
  await page.getByLabel("Customer phone", { exact: true }).fill(manualCustomerPhone);
  await page.getByRole("button", { name: "Find eligible offers", exact: true }).click();
  await expect(page.getByRole("radio")).toHaveCount(1);
  await expect(page.getByRole("radio")).toBeChecked();
  await expect(page.getByText("Browser UGC Single", { exact: false })).toBeVisible();

  const first = await createPublishedUgc(context, `Browser UGC A ${Date.now()}`, "creator");
  const second = await createPublishedUgc(context, `Browser UGC B ${Date.now()}`, "creator");
  await login(context, "business");
  await open(page, "/checkout");
  await page.getByRole("button", { name: "Enter Manually", exact: true }).click();
  await page.getByLabel("Creator ID", { exact: true }).fill("CR-100");
  await page.getByLabel("Customer phone", { exact: true }).fill(manualCustomerPhone);
  await page.getByRole("button", { name: "Find eligible offers", exact: true }).click();
  await expect(page.getByRole("radio")).toHaveCount(3);
  await expect(page.getByText("Browser UGC B", { exact: false })).toBeVisible();
  const beforeFirst = await apiJson<{ opportunity: { customerOfferRemaining?: number } }>(context, `/business/ugc/${first}`);
  const beforeSecond = await apiJson<{ opportunity: { customerOfferRemaining?: number } }>(context, `/business/ugc/${second}`);
  const selectedOffer = page.locator("label.choice-row").filter({ hasText: "Browser UGC B" }).getByRole("radio");
  await selectedOffer.check();
  const selectedOfferId = await selectedOffer.inputValue();

  // The database integrity trigger requires a UGC Customer Offer sale to carry
  // an issued Customer QR reference. Manual discovery still chooses the offer
  // explicitly; settlement then uses the same authoritative QR checkout path.
  await login(context, "customer");
  const issued = await apiPost(context, `/customer/offers/${selectedOfferId}/qr`, undefined);
  expect(issued.ok()).toBeTruthy();
  const qr = (await issued.json()) as { token: string };
  await login(context, "business");
  await open(page, "/checkout");
  await page.getByText("Enter an opaque QR code", { exact: true }).click();
  await page.getByLabel("QR code", { exact: true }).fill(qr.token);
  await page.getByRole("button", { name: "Resolve QR", exact: true }).click();
  await expect(page.getByLabel("Total Purchase Amount", { exact: true })).toBeVisible();
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
  await expect(page.getByRole("button", { name: "Profile", exact: true }).first()).toBeVisible();
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
    .getByRole("link", { name: "Get Offer", exact: true }).click();
  const issuing = page.waitForResponse((response) => response.url().endsWith("/qr") && response.request().method() === "POST");
  await page.getByRole("button", { name: "Get Offer", exact: true }).click();
  const issued = await (await issuing).json() as { token: string };
  const expireResponse = await context.request.post("/__test/qr/expired", {
    headers: { ...requestHeaders, "Idempotency-Key": crypto.randomUUID() },
    data: { token: issued.token },
  });
  expect(expireResponse.ok()).toBeTruthy();
  const expired = (await expireResponse.json()) as { token: string };
  await login(context, "business");
  await open(page, "/checkout");
  await page.getByText("Enter an opaque QR code", { exact: true }).click();
  await page.getByLabel("QR code", { exact: true }).fill(expired.token);
  await page.getByRole("button", { name: "Resolve QR", exact: true }).click();
  await expect(page.getByRole("alert")).toHaveText("QR code expired");

  await login(context, "customer");
  await open(page, "/customer/offers");
  await page.getByRole("link", { name: "Get Offer", exact: true }).first().click();
  const secondIssuing = page.waitForResponse((response) => response.url().endsWith("/qr") && response.request().method() === "POST");
  await page.getByRole("button", { name: "Get Offer", exact: true }).click();
  const ineligible = await (await secondIssuing).json() as { token: string };
  const ineligibleResponse = await context.request.post("/__test/qr/not-eligible", {
    headers: { ...requestHeaders, "Idempotency-Key": crypto.randomUUID() },
    data: { token: ineligible.token },
  });
  expect(ineligibleResponse.status()).toBe(204);
  await login(context, "business");
  await open(page, "/checkout");
  await page.getByText("Enter an opaque QR code", { exact: true }).click();
  await page.getByLabel("QR code", { exact: true }).fill(ineligible.token);
  await page.getByRole("button", { name: "Resolve QR", exact: true }).click();
  await expect(page.getByRole("alert")).toHaveText("Offer is no longer available");
});

test("Business and Cashier checkout layouts stay usable at required widths", async ({ page, context }) => {
  for (const width of [360, 390, 430, 768, 1366, 1920]) {
    await page.setViewportSize({ width, height: 900 });
    await login(context, "business");
    await open(page, "/business");
    await layout(page);
    await expect(page.getByRole("link", { name: "Checkout / Scan QR", exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: "Cashier Management", exact: true })).toBeVisible();
    await login(context, "cashier");
    await open(page, "/checkout");
    await layout(page);
    await expect(page.getByRole("button", { name: "Scan QR", exact: true })).toBeVisible();
  }
});
