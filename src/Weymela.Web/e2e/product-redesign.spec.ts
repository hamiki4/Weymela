import { expect, test, type BrowserContext } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";

const testHeaders = { "X-Weymela-Request": "1" };

async function apiJson<T>(context: BrowserContext, path: string): Promise<T> {
  const response = await context.request.get(`/api${path}`, { headers: testHeaders });
  expect(response.ok(), `${path}: ${response.status()}`).toBeTruthy();
  return await response.json() as T;
}

async function apiPost<T>(context: BrowserContext, path: string, data?: unknown): Promise<T> {
  const response = await context.request.post(`/api${path}`, {
    headers: { ...testHeaders, "Idempotency-Key": crypto.randomUUID() },
    data,
  });
  expect(response.ok(), `${path}: ${response.status()} ${await response.text()}`).toBeTruthy();
  return await response.json() as T;
}

async function ensureVisualReviewUgc(context: BrowserContext) {
  await login(context, "business");
  const opportunities = await apiJson<{ id: string; title: string; status: string }[]>(context, "/business/ugc");
  let opportunity = opportunities.find(row => row.title.startsWith("UGC slots 320-") && !["Draft", "Cancelled"].includes(row.status));
  if (!opportunity) {
    const title = `UGC slots 320-${Date.now()}`;
    const created = await apiPost<{ id: string }>(context, "/business/ugc", {
      title,
      slogan: null,
      contentType: "Video",
      instructions: "Create a short product video.",
      resources: [],
      location: "Addis Ababa",
      dueDateUtc: new Date(Date.now() + 14 * 86400000).toISOString(),
      productProvided: true,
      creatorMustPurchase: false,
      usageRights: null,
      creatorPayment: 500,
      creatorsNeeded: 1,
      platformRequirements: [{ platform: "TikTok", format: "Social post", minimumAudience: null }],
      platformCapacities: [{ platform: "TikTok", capacity: 1, minimumAudience: null }],
      customerOfferEnabled: false,
    });
    const createdDetail = await apiJson<{ opportunity: { version: number } }>(context, `/business/ugc/${created.id}`);
    await apiPost(context, `/business/ugc/${created.id}/publish`, { version: createdDetail.opportunity.version });
    opportunity = { id: created.id, title, status: "Open" };
  }
  const opportunityId = opportunity.id;
  const detail = await apiJson<{ opportunity: { id: string; version: number; status: string }; instructions: string; requests: { id: string; creator: string; status: string }[] }>(
    context,
    `/business/ugc/${opportunityId}`,
  );

  await login(context, "creator");
  const assignments = await apiJson<{ opportunityId: string }[]>(context, "/creator/ugc/assignments");
  if (!assignments.some(row => row.opportunityId === opportunityId)) {
    const creatorDetail = await apiJson<{ opportunity: {
      eligibleSocialProfiles?: { id: string; platform: string }[];
      platformCapacities?: { platform: string; available: number }[];
    } }>(context, `/creator/ugc/${opportunityId}`);
    const profile = creatorDetail.opportunity.eligibleSocialProfiles?.find(candidate =>
      creatorDetail.opportunity.platformCapacities?.some(slot => slot.platform === candidate.platform && slot.available > 0),
    );
    if (!profile) throw new Error("The visual-review UGC fixture has no available verified Creator platform.");
    const requests = await apiJson<{ id: string; opportunityId: string; status: string }[]>(context, "/creator/ugc/requests");
    const existing = requests.find(row => row.opportunityId === opportunityId && ["Pending", "Approved"].includes(row.status));
    if (!existing) await apiPost<{ id: string }>(context, `/creator/ugc/${opportunityId}/request?selectedPlatform=${encodeURIComponent(profile.platform)}&verifiedSocialProfileId=${profile.id}`);

    await login(context, "business");
    const current = await apiJson<{ requests: { id: string; creator: string; status: string }[] }>(
      context,
      `/business/ugc/${opportunityId}`,
    );
    const pending = current.requests.find(row => row.creator === "Bella" && row.status === "Pending");
    if (pending) await apiPost(context, `/business/ugc/requests/${pending.id}/approve`, { reason: null });
  }
  await login(context, "creator");
  return { title: opportunity.title, instructions: detail.instructions };
}

test("Customer, Creator and Business use compact horizontal navigation on desktop", async ({ page, context }) => {
  await page.setViewportSize({ width: 1366, height: 900 });
  for (const [role, path, label] of [["customer", "/customer/offers", "Discover"], ["creator", "/creator", "Promotions"], ["business", "/business", "Wallet"]]) {
    await login(context, role);
    await open(page, path);
    await expect(page.locator(".product-desktop-nav").getByRole("link", { name: label, exact: true })).toBeVisible();
    await expect(page.locator(".sidebar")).not.toBeVisible();
    await layout(page);
    await screenshot(page, `product-desktop-${role}`);
  }
});

test("Customer mobile Home shows real account information and useful offer links", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(context, "customer");
  await open(page, "/customer/offers");
  await expect(page.getByRole("link", { name: /Available cashback/i })).toHaveAttribute("href", "/customer/cashback");
  const nav = page.getByRole("navigation", { name: "Mobile navigation" });
  await expect(nav.getByRole("link", { name: "Cashback" })).toBeVisible();
  await expect(nav.getByRole("link", { name: "Transactions" })).toBeVisible();
  await layout(page);
  await screenshot(page, "product-mobile-customer");
});

test("Creator Home keeps the next destination clear across mobile and desktop", async ({ page, context }) => {
  await login(context, "creator");

  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, "/creator");
  await expect(page.getByRole("link", { name: "Discover Promotions" })).toHaveAttribute(
    "href",
    "/creator/discover",
  );
  await expect(page.getByRole("heading", { name: "Your work" })).toBeVisible();
  const home = page.locator(".creator-home");
  await expect(home.getByText("Pending Requests", { exact: true })).toBeVisible();
  await expect(home.getByText("Live Promotions", { exact: true })).toBeVisible();
  await expect(home.getByText("Available Earnings", { exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "My Promotions" })).toHaveAttribute(
    "href",
    "/creator/promotions",
  );
  await layout(page);
  await screenshot(page, "creator-home-390");

  await page.setViewportSize({ width: 320, height: 800 });
  await open(page, "/creator");
  await expect(home.getByText("Pending Requests", { exact: true })).toBeVisible();
  await expect(home.getByText("Live Promotions", { exact: true })).toBeVisible();
  await expect(home.getByText("Available Earnings", { exact: true })).toBeVisible();
  await layout(page);
  await screenshot(page, "creator-home-320");

  await page.setViewportSize({ width: 1366, height: 768 });
  await open(page, "/creator");
  await expect(page.getByRole("link", { name: "Discover Promotions" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Earnings" })).toBeVisible();
  await layout(page);
  await screenshot(page, "creator-home-1366");
});

test("Creator Promotions uses three action-oriented groups", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(context, "creator");
  await open(page, "/creator/promotions");
  await expect(page.getByRole("heading", { name: "My Promotions" })).toBeVisible();
  const tabs = page.getByRole("tablist", { name: "Filter Promotions" });
  await expect(tabs.getByRole("tab")).toHaveCount(3);
  for (const name of ["Pending", "Active", "Completed"]) await expect(tabs.getByRole("tab", { name })).toBeVisible();
  await layout(page);
  await screenshot(page, "creator-promotions-mobile-final");
  await tabs.getByRole("tab", { name: "Pending" }).click();
  await expect(tabs.getByRole("tab", { name: "Pending" })).toHaveAttribute("aria-selected", "true");
  await layout(page);
  await tabs.getByRole("tab", { name: "Completed" }).click();
  await expect(tabs.getByRole("tab", { name: "Completed" })).toHaveAttribute("aria-selected", "true");
  await layout(page);
  await page.setViewportSize({ width: 1366, height: 768 });
  await open(page, "/creator/promotions");
  await layout(page);
  await screenshot(page, "creator-promotions-desktop-final");
});

test("Creator visual review captures Discover and compact work states", async ({ page, context }) => {
  await login(context, "creator");

  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, "/creator/promotions?filter=Active");
  const regularCards = page.locator("article.creator-promotion-row").filter({
    has: page.locator(".creator-work-type").filter({ hasText: /^(Views|Views \+ Sales)$/ }),
  });
  await expect(regularCards).not.toHaveCount(0);
  await layout(page);
  await screenshot(page, "creator-promotions-regular-390");

  await page.setViewportSize({ width: 1366, height: 768 });
  await open(page, "/creator/promotions?filter=Active");
  await expect(regularCards).not.toHaveCount(0);
  await layout(page);
  await screenshot(page, "creator-promotions-regular-1366");

  const visualUgc = await ensureVisualReviewUgc(context);

  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, "/creator/discover");
  await expect(page.getByRole("heading", { name: visualUgc.title })).toBeVisible();
  await layout(page);
  await screenshot(page, "creator-discover-390");

  await page.setViewportSize({ width: 1366, height: 768 });
  await open(page, "/creator/discover");
  await expect(page.getByRole("heading", { name: visualUgc.title })).toBeVisible();
  await layout(page);
  await screenshot(page, "creator-discover-1366");

  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, "/creator/promotions?filter=Active");
  const mobileUgcCard = page.locator(".creator-work-row").filter({ hasText: visualUgc.title });
  await expect(mobileUgcCard).toHaveCount(1);
  await layout(page);
  await screenshot(page, "creator-promotions-ugc-390");
  await mobileUgcCard.locator("summary").click();
  await expect(mobileUgcCard.getByText(visualUgc.instructions, { exact: true })).toBeVisible();
  await layout(page);
  await screenshot(page, "creator-promotions-ugc-details-390");

  await page.setViewportSize({ width: 1366, height: 768 });
  await open(page, "/creator/promotions?filter=Active");
  const desktopUgcCard = page.locator(".creator-work-row").filter({ hasText: visualUgc.title });
  await expect(desktopUgcCard).toHaveCount(1);
  await layout(page);
  await screenshot(page, "creator-promotions-ugc-1366");
  await desktopUgcCard.locator("summary").click();
  await expect(desktopUgcCard.getByText(visualUgc.instructions, { exact: true })).toBeVisible();
  await layout(page);
  await screenshot(page, "creator-promotions-ugc-details-1366");

  await page.setViewportSize({ width: 320, height: 800 });
  await open(page, "/creator/promotions?filter=Active");
  await layout(page);
  await screenshot(page, "creator-overflow-320");
});

test("Business keeps funding, Checkout and Cashier Management within reach", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(context, "business");
  await open(page, "/business");
  await expect(page.getByRole("link", { name: /Available funds/i })).toHaveAttribute("href", "/business/wallet");
  await page.getByRole("link", { name: /Active Promotions/ }).click();
  await expect(page).toHaveURL(/\/business\/campaigns\?filter=Active$/);
  await expect(page.getByRole("heading", { name: "Active Promotions", exact: true })).toBeVisible();
  await page.getByRole("link", { name: "Create Promotion" }).click();
  await expect(page).toHaveURL(/\/business\/campaigns\/new$/);
  await expect(page.getByText("UGC + Sales", { exact: true })).toBeVisible();
  await open(page, "/business");
  const nav = page.getByRole("navigation", { name: "Mobile navigation" });
  await expect(nav.getByRole("link", { name: "Wallet" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Checkout / Scan QR" })).toHaveAttribute("href", "/checkout");
  await page.getByRole("button", { name: "Open Settings" }).click();
  const settings = page.getByRole("dialog", { name: "Settings" });
  await expect(settings.getByRole("link", { name: "Cashier Management" })).toHaveAttribute("href", "/business/cashiers");
  await expect(settings.getByRole("link", { name: "Create Cashier" })).toHaveCount(0);
  await settings.getByRole("button", { name: "Close Settings" }).click();
  await nav.getByRole("link", { name: "Profile" }).click();
  await expect(page).toHaveURL(/\/profile$/);
  await expect(page.getByRole("heading", { name: "Business Information" })).toBeVisible();
  await expect(settings).not.toBeVisible();
  await layout(page);
  await screenshot(page, "product-mobile-business");
});

test("Business visual review captures operational surfaces at approved widths", async ({ page, context }) => {
  const visualUgc = await ensureVisualReviewUgc(context);
  await login(context, "business");
  const campaigns = await apiJson<{ id: string; title: string; status: string }[]>(context, "/business/campaigns");
  const detailCampaign = campaigns.find((row) => !["Draft", "Cancelled"].includes(row.status));
  expect(detailCampaign, "an existing published Business Promotion is required for visual evidence").toBeDefined();

  const assertMobileNavigation = async (height: number) => {
    const navigation = page.getByRole("navigation", { name: "Mobile navigation" });
    const bounds = await navigation.boundingBox();
    expect(bounds).not.toBeNull();
    expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(height + 1);
  };

  await page.setViewportSize({ width: 320, height: 800 });
  await open(page, "/business");
  await layout(page);
  await assertMobileNavigation(800);
  await screenshot(page, "business-home-320");
  await screenshot(page, "business-overflow-320");

  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, "/business");
  await layout(page);
  await assertMobileNavigation(844);
  await screenshot(page, "business-home-390");

  await page.setViewportSize({ width: 320, height: 800 });
  await open(page, "/business/campaigns");
  const promotionsHeading = page.locator("main h2").filter({ hasText: /^Promotions$/ });
  await expect(promotionsHeading).toHaveCount(1);
  await expect(page.locator(".business-promotion-card").filter({ hasText: visualUgc.title })).toHaveCount(1);
  await layout(page);
  await screenshot(page, "business-promotions-320");

  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, "/business/campaigns");
  await expect(promotionsHeading).toHaveCount(1);
  await expect(page.locator(".business-promotion-card").filter({ hasText: visualUgc.title })).toHaveCount(1);
  await layout(page);
  await screenshot(page, "business-promotions-390");

  await open(page, "/business/campaigns/new");
  await expect(page.getByRole("heading", { name: "Create Promotion", exact: true })).toBeVisible();
  await expect(page.getByText("UGC + Sales", { exact: true })).toBeVisible();
  await layout(page);
  await screenshot(page, "business-create-390");

  await open(page, `/business/campaigns/${detailCampaign!.id}`);
  await expect(page.getByRole("heading", { name: detailCampaign!.title, exact: true })).toBeVisible();
  await layout(page);
  await screenshot(page, "business-promotion-detail-390");

  await open(page, "/business/requests");
  await expect(page.getByRole("heading", { name: "Creator Requests", exact: true })).toBeVisible();
  await layout(page);
  await screenshot(page, "business-requests-390");

  await open(page, "/business/wallet");
  await page.locator("summary[aria-label='Open Add Funds']").click();
  await expect(page.getByLabel("Amount", { exact: true })).toBeVisible();
  await layout(page);
  await screenshot(page, "business-wallet-390");

  await page.setViewportSize({ width: 320, height: 800 });
  await open(page, "/business/wallet");
  await page.locator("summary[aria-label='Open Add Funds']").click();
  await expect(page.getByLabel("Amount", { exact: true })).toBeVisible();
  await layout(page);
  await screenshot(page, "business-wallet-320");

  await page.setViewportSize({ width: 1366, height: 768 });
  await open(page, "/business");
  const fundValues = page.locator(".business-home-fund strong");
  await expect(fundValues).toHaveCount(2);
  const fundBounds = await fundValues.evaluateAll((elements) => elements.map((element) => {
    const bounds = element.getBoundingClientRect();
    return { left: bounds.left, right: bounds.right };
  }));
  for (let index = 1; index < fundBounds.length; index += 1) {
    expect(fundBounds[index - 1].right).toBeLessThanOrEqual(fundBounds[index].left + 1);
  }
  await layout(page);
  await screenshot(page, "business-home-1366");

  await open(page, "/business/campaigns");
  await expect(page.locator(".business-promotion-card").filter({ hasText: visualUgc.title })).toHaveCount(1);
  await layout(page);
  await screenshot(page, "business-promotions-1366");

  await open(page, "/business/campaigns/new");
  await expect(page.getByText("UGC + Sales", { exact: true })).toBeVisible();
  await layout(page);
  await screenshot(page, "business-create-1366");

  await open(page, `/business/campaigns/${detailCampaign!.id}`);
  await layout(page);
  await screenshot(page, "business-promotion-detail-1366");

  await open(page, "/business/requests");
  await layout(page);
  await screenshot(page, "business-requests-1366");

  await open(page, "/business/wallet");
  await page.locator("summary[aria-label='Open Add Funds']").click();
  await expect(page.getByLabel("Amount", { exact: true })).toBeVisible();
  await layout(page);
  await screenshot(page, "business-wallet-1366");
});

test("Cashier has Purchase, Transactions and Profile without a dashboard", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(context, "cashier");
  await open(page, "/checkout");
  const nav = page.getByRole("navigation", { name: "Mobile navigation" });
  await expect(nav.getByRole("link", { name: "Purchase" })).toBeVisible();
  await nav.getByRole("link", { name: "Transactions" }).click();
  await expect(page).toHaveURL(/\/checkout\/transactions$/);
  await expect(page.getByRole("heading", { name: "Recent purchases" })).toBeVisible();
  await expect(nav.getByRole("button", { name: "Profile" })).toBeVisible();
  await layout(page);
  await screenshot(page, "product-mobile-cashier-transactions");
});
