import { expect, test, type BrowserContext } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";

const headers = { "X-Weymela-Request": "1" };
type Slot = { platform: string; capacity: number; approved: number; available: number };
type Detail = { opportunity: { version: number; platformCapacities: Slot[]; approvedCreators: number }; requests: { id: string; status: string }[] };

async function apiJson<T>(context: BrowserContext, path: string): Promise<T> {
  const response = await context.request.get(`/api${path}`, { headers });
  expect(response.ok(), `${path}: ${response.status()}`).toBeTruthy();
  return await response.json() as T;
}

async function apiPost(context: BrowserContext, path: string, data?: unknown) {
  const response = await context.request.post(`/api${path}`, {
    headers: { ...headers, "Idempotency-Key": crypto.randomUUID() }, data,
  });
  expect(response.ok(), `${path}: ${response.status()} ${await response.text()}`).toBeTruthy();
  return response;
}

async function createPublished(context: BrowserContext, title: string, capacities: { platform: string; capacity: number }[], creators: number) {
  await login(context, "business");
  const response = await apiPost(context, "/business/ugc", {
    title, slogan: null, contentType: "Video", instructions: "Create a short product video.",
    resources: [], location: "Addis Ababa", dueDateUtc: new Date(Date.now() + 14 * 86400000).toISOString(),
    productProvided: true, creatorMustPurchase: false, usageRights: null,
    creatorPayment: 250, creatorsNeeded: creators,
    platformRequirements: capacities.map(({ platform }) => ({ platform, format: "Social post", minimumAudience: null })),
    platformCapacities: capacities, customerOfferEnabled: false,
  });
  const { id } = await response.json() as { id: string };
  const detail = await apiJson<Detail>(context, `/business/ugc/${id}`);
  await apiPost(context, `/business/ugc/${id}/publish`, { version: detail.opportunity.version });
  return id;
}

test("posted UGC binds verified profile and disables full platforms", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const title = `Posted UGC slots ${Date.now()}`;
  const id = await createPublished(context, title, [{ platform: "TikTok", capacity: 1 }, { platform: "Instagram", capacity: 1 }], 2);

  await login(context, "creator");
  await open(page, "/creator/discover");
  const card = page.locator(".creator-opportunity-card").filter({ hasText: title });
  await expect(card.getByRole("radio")).toHaveCount(2);
  await expect(card.locator('[aria-label*="filled"]')).toHaveCount(0);
  await expect(card.getByRole("button", { name: "Request to Join" })).toBeDisabled();
  await card.getByRole("radio", { name: /TikTok/ }).check();
  const joinButton = card.getByRole("button", { name: "Request to Join" });
  await joinButton.evaluate(element => element.scrollIntoView({ block: "center" }));
  const joinBounds = await joinButton.boundingBox();
  const navigationBounds = await page.getByRole("navigation", { name: "Mobile navigation" }).boundingBox();
  expect(joinBounds && navigationBounds && joinBounds.y + joinBounds.height < navigationBounds.y).toBeTruthy();
  await layout(page);
  await screenshot(page, "390-creator-ugc-platform-selection");
  const firstRequest = page.waitForResponse(response => response.request().method() === "POST" && response.url().includes(`/api/creator/ugc/${id}/request`));
  await card.getByRole("button", { name: "Request to Join" }).click();
  const firstResponse = await firstRequest;
  expect(firstResponse.ok()).toBeTruthy();
  const firstUrl = new URL(firstResponse.url());
  expect(firstUrl.searchParams.get("selectedPlatform")).toBe("TikTok");
  expect(firstUrl.searchParams.get("verifiedSocialProfileId")).toMatch(/^[a-f0-9-]{36}$/);

  await login(context, "business");
  const pending = await apiJson<Detail>(context, `/business/ugc/${id}`);
  expect(pending.opportunity.platformCapacities.find(slot => slot.platform === "TikTok")?.approved).toBe(0);
  const first = pending.requests.find(request => request.status === "Pending");
  expect(first).toBeDefined();
  await apiPost(context, `/business/ugc/requests/${first!.id}/approve`, { reason: null });
  const oneApproved = await apiJson<Detail>(context, `/business/ugc/${id}`);
  expect(oneApproved.opportunity.platformCapacities.find(slot => slot.platform === "TikTok")?.approved).toBe(1);
  expect(oneApproved.opportunity.platformCapacities.find(slot => slot.platform === "Instagram")?.approved).toBe(0);

  await login(context, "other-creator");
  const visibleToOtherCreator = await apiJson<{ id: string; title: string }[]>(context, "/creator/ugc");
  expect(visibleToOtherCreator.some(item => item.id === id), JSON.stringify(visibleToOtherCreator.map(item => item.title))).toBeTruthy();
  await open(page, "/creator/discover");
  const remaining = page.locator(".creator-opportunity-card").filter({ hasText: title });
  await expect(remaining.locator('[aria-label*="filled"]')).toHaveCount(0);
  await expect(remaining.getByRole("radio")).toHaveCount(0);
  await expect(remaining.getByText("Verified Instagram profile")).toBeVisible();
  await expect(remaining.getByRole("button", { name: "Request to Join" })).toBeEnabled();
  const secondRequest = page.waitForResponse(response => response.request().method() === "POST" && response.url().includes(`/api/creator/ugc/${id}/request`));
  await remaining.getByRole("button", { name: "Request to Join" }).click();
  const secondResponse = await secondRequest;
  expect(secondResponse.ok()).toBeTruthy();
  expect(new URL(secondResponse.url()).searchParams.get("selectedPlatform")).toBe("Instagram");

  await login(context, "business");
  const secondPending = await apiJson<Detail>(context, `/business/ugc/${id}`);
  expect(secondPending.opportunity.platformCapacities.find(slot => slot.platform === "Instagram")?.approved).toBe(0);
  const second = secondPending.requests.find(request => request.status === "Pending");
  expect(second).toBeDefined();
  await apiPost(context, `/business/ugc/requests/${second!.id}/approve`, { reason: null });
  const complete = await apiJson<Detail>(context, `/business/ugc/${id}`);
  expect(complete.opportunity.platformCapacities.map(slot => [slot.platform, slot.approved, slot.available]).sort()).toEqual([
    ["Instagram", 1, 0], ["TikTok", 1, 0],
  ]);

  await login(context, "ineligible-creator");
  await page.setViewportSize({ width: 393, height: 852 });
  const unavailable = await apiJson<{ id: string }[]>(context, "/creator/ugc");
  expect(unavailable.some(item => item.id === id)).toBe(false);
  await open(page, "/creator/discover");
  await expect(page.locator(".creator-opportunity-card").filter({ hasText: title })).toHaveCount(0);
  await layout(page);
  await screenshot(page, "393-creator-ugc-all-full");
});

test("delivery-only UGC joins without a social-platform binding", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const title = `Delivery UGC ${Date.now()}`;
  const id = await createPublished(context, title, [], 1);
  await login(context, "ineligible-creator");
  await open(page, "/creator/discover");
  const card = page.locator(".creator-opportunity-card").filter({ hasText: title });
  await expect(card.getByRole("radio")).toHaveCount(0);
  await expect(card.getByRole("button", { name: "Request to Join" })).toBeEnabled();
  const joining = page.waitForResponse(response => response.request().method() === "POST" && response.url().includes(`/api/creator/ugc/${id}/request`));
  await card.getByRole("button", { name: "Request to Join" }).click();
  const joined = await joining;
  expect(joined.ok()).toBeTruthy();
  const url = new URL(joined.url());
  expect(url.searchParams.has("selectedPlatform")).toBe(false);
  expect(url.searchParams.has("verifiedSocialProfileId")).toBe(false);
  await login(context, "business");
  const pending = await apiJson<Detail>(context, `/business/ugc/${id}`);
  expect(pending.opportunity.platformCapacities).toEqual([]);
  expect(pending.opportunity.approvedCreators).toBe(0);
  await apiPost(context, `/business/ugc/requests/${pending.requests.find(request => request.status === "Pending")!.id}/approve`, { reason: null });
  expect((await apiJson<Detail>(context, `/business/ugc/${id}`)).opportunity.approvedCreators).toBe(1);
});

test("self-reported Facebook link qualifies when audience enforcement is off", async ({ context }) => {
  const title = `Self-reported Facebook ${Date.now()}`;
  const id = await createPublished(context, title, [{ platform: "Facebook", capacity: 1 }], 1);
  await login(context, "creator");
  const linked = await apiPost(context, "/creator/social-profiles/Facebook", { profileUrl: "https://www.facebook.com/bella" });
  const { id: profileId } = await linked.json() as { id: string };
  const discover = await apiJson<{ id: string }[]>(context, "/creator/ugc");
  expect(discover.some(item => item.id === id)).toBe(true);
  const joined = await context.request.post(`/api/creator/ugc/${id}/request?selectedPlatform=Facebook&verifiedSocialProfileId=${profileId}`, {
    headers: { ...headers, "Idempotency-Key": crypto.randomUUID() },
  });
  expect(joined.ok()).toBe(true);
});
