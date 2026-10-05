import { readFileSync } from "node:fs";
import { expect, test, type Locator } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";

const png = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAEklEQVR4AWIqjpn8H4SZGKAAAAAA//+9j9SYAAAABklEQVQDAD6oBMcQkXozAAAAAElFTkSuQmCC", "base64");

// BrowserHost's development headers differ from the shipped Web document policy.
// Exercise the checked-in Web CSP in regular CI as well as behind real Nginx.
const webCsp = readFileSync("../../docker/web/security-headers.conf", "utf8")
  .match(/Content-Security-Policy "([^"]+)" always;/)![1];
async function rendered(image: Locator, width = 2) {
  await expect(image).toBeVisible();
  await expect.poll(() => image.evaluate((element: HTMLImageElement) =>
    element.complete ? element.naturalWidth : 0)).toBe(width);
}

test("Creator photo changes on Profile and is visible only through Business Creator review", async ({ page, context }) => {
  const imageViolations: string[] = [];
  await page.exposeFunction("recordPhotoCspViolation", (uri: string) => imageViolations.push(uri));
  await page.addInitScript(() => document.addEventListener("securitypolicyviolation", event => {
    if (event.effectiveDirective === "img-src")
      void (window as unknown as { recordPhotoCspViolation(uri: string): Promise<void> })
        .recordPhotoCspViolation(event.blockedURI);
  }));
  await page.route("**/*", async route => {
    if (route.request().resourceType() !== "document") return route.continue();
    const response = await route.fetch();
    await route.fulfill({ response, headers: { ...response.headers(), "content-security-policy": webCsp } });
  });
  await login(context, "creator");
  await open(page, "/profile");
  await expect(page.getByRole("button", { name: "Add photo" })).toBeVisible();
  await page.getByLabel("Choose Creator profile photo").setInputFiles({ name: "creator.png", mimeType: "image/png", buffer: png });
  await rendered(page.getByAltText("Selected profile photo preview"));
  await page.getByRole("button", { name: "Save photo" }).click();
  await expect(page.getByText("Photo updated.")).toBeVisible();
  await rendered(page.locator(".profile-avatar img"));
  await page.reload();
  await rendered(page.locator(".profile-avatar img"));
  // A different decoded image proves replacement, rather than a cached old object URL.
  const jpeg = Buffer.from(await page.evaluate(() => {
    const canvas = document.createElement("canvas"); canvas.width = 3; canvas.height = 3;
    canvas.getContext("2d")!.fillRect(0, 0, 3, 3);
    return canvas.toDataURL("image/jpeg").split(",")[1];
  }), "base64");
  await page.getByLabel("Choose Creator profile photo").setInputFiles({ name: "replacement.jpg", mimeType: "image/jpeg", buffer: jpeg });
  await rendered(page.getByAltText("Selected profile photo preview"), 3);
  const replacement = page.waitForResponse(response => response.url().endsWith("/api/creator/photo") && response.request().method() === "POST");
  await page.getByRole("button", { name: "Save photo" }).click();
  expect((await replacement).status()).toBe(200);
  await rendered(page.locator(".profile-avatar img"), 3);
  await page.reload();
  await rendered(page.locator(".profile-avatar img"), 3);
  for (const width of [320, 360, 375, 390, 393, 430]) {
    await page.setViewportSize({ width, height: 844 });
    await layout(page);
    if (width === 390 || width === 393) await screenshot(page, `creator-photo-profile-${width}`);
  }
  await login(context, "business");
  const campaigns = await context.request.get("/api/business/campaigns");
  expect(campaigns.ok()).toBeTruthy();
  const rows = await campaigns.json() as { id: string; status: string }[];
  let creatorCampaign: string | null = null;
  for (const row of rows.filter(item => item.status === "Active")) {
    const candidate = await context.request.get(`/api/business/campaigns/${row.id}`);
    if (!candidate.ok()) continue;
    const detail = await candidate.json() as { applicants: { creator: { displayName: string } }[] };
    if (detail.applicants.some(applicant => applicant.creator.displayName === "Bella")) {
      creatorCampaign = row.id; break;
    }
  }
  expect(creatorCampaign).not.toBeNull();
  await open(page, `/business/campaigns/${creatorCampaign}?tab=applicants`);
  await expect(page.getByRole("heading", { name: "Creator Applicants" })).toBeVisible();
  const applicants = page.locator("section.panel")
    .filter({ has: page.getByRole("heading", { name: "Creator Applicants", exact: true }) });
  for (const width of [320, 360, 375, 390, 393, 430]) {
    await page.setViewportSize({ width, height: 844 });
    await expect(applicants.locator(".person").filter({ hasText: "Bella" })
      .locator(".creator-photo-avatar img:visible")).toHaveCount(1);
    await rendered(applicants.locator(".person:visible").filter({ hasText: "Bella" })
      .locator(".creator-photo-avatar img"), 3);
    await layout(page);
    if (width === 390 || width === 393) await screenshot(page, `business-creator-photo-review-${width}`);
  }
  await page.getByRole("tab", { name: "Approved Creators" }).click();
  const approvedCreators = page.locator("section.panel")
    .filter({ has: page.getByRole("heading", { name: "Approved Creators", exact: true }) });
  await expect(approvedCreators.locator(".person:visible").filter({ hasText: "Bella" })
    .locator(".creator-photo-avatar img:visible")).toHaveCount(1);
  const title = `Creator photo UGC ${Date.now()}`;
  const headers = { "X-Weymela-Request": "1", "Idempotency-Key": crypto.randomUUID() };
  const created = await context.request.post("/api/business/ugc", { headers, data: {
    title, slogan: null, contentType: "Video", instructions: "Create a product video.", resources: [], location: "Addis Ababa",
    dueDateUtc: new Date(Date.now() + 14 * 86400000).toISOString(), productProvided: true,
    creatorMustPurchase: false, usageRights: null, creatorPayment: 250, creatorsNeeded: 1,
    platformRequirements: [{ platform: "TikTok", format: "Social post", minimumAudience: null }],
    platformCapacities: [{ platform: "TikTok", capacity: 1 }], customerOfferEnabled: false,
  } });
  expect(created.ok(), await created.text()).toBeTruthy();
  const { id: ugcId } = await created.json() as { id: string };
  const initial = await context.request.get(`/api/business/ugc/${ugcId}`, { headers: { "X-Weymela-Request": "1" } });
  expect(initial.ok()).toBeTruthy();
  const detail = await initial.json() as { opportunity: { version: number } };
  const published = await context.request.post(`/api/business/ugc/${ugcId}/publish`, { headers: { ...headers, "Idempotency-Key": crypto.randomUUID() }, data: { version: detail.opportunity.version } });
  expect(published.ok(), await published.text()).toBeTruthy();
  await login(context, "creator");
  await open(page, "/creator/discover");
  const opportunity = page.locator(".creator-opportunity-card").filter({ hasText: title });
  const joined = page.waitForResponse(response => response.request().method() === "POST" && response.url().includes(`/api/creator/ugc/${ugcId}/request`));
  await opportunity.getByRole("button", { name: "Request to Join" }).click();
  expect((await joined).ok()).toBeTruthy();
  await login(context, "business");
  await open(page, "/business/campaigns");
  const ugcCard = page.locator(".data-card").filter({ hasText: title });
  await ugcCard.getByRole("button", { name: "Manage" }).click();
  await ugcCard.getByRole("button", { name: "Creator requests & assignments" }).click();
  await rendered(ugcCard.locator(".business-ugc-creator-row .creator-photo-avatar img"), 3);
  for (const width of [320, 360, 375, 390, 393, 430]) {
    await page.setViewportSize({ width, height: 844 });
    await layout(page);
    if (width === 390 || width === 393) await screenshot(page, `business-ugc-creator-photo-${width}`);
  }
  await ugcCard.getByRole("button", { name: "Approve" }).click();
  await expect(ugcCard.getByRole("heading", { name: "Approved Creators" })).toBeVisible();
  await expect(ugcCard.locator(".business-ugc-creator-row .creator-photo-avatar img")).toHaveCount(2);
  await login(context, "creator");
  await open(page, "/profile");
  const creatorId = "00000000-0000-4000-8000-000000000300";
  const businessPhoto = `/api/business/creator-photos/${creatorId}`;
  for (const alias of ["customer", "business", "cashier", "admin", "operations-admin"]) {
    await login(context, alias);
    expect((await context.request.post("/api/creator/photo", { headers: { "X-Weymela-Request": "1" },
      multipart: { photo: { name: "denied.png", mimeType: "image/png", buffer: png } } })).status()).toBe(403);
    expect((await context.request.delete("/api/creator/photo", { headers: { "X-Weymela-Request": "1" } })).status()).toBe(403);
    if (alias === "customer") expect((await context.request.get(businessPhoto)).status()).toBe(403);
  }
  await login(context, "other-business");
  expect((await context.request.get(businessPhoto)).status()).toBe(404);
  await login(context, "other-creator");
  expect((await context.request.get("/api/creator/photo")).status()).toBe(404);
  expect((await context.request.get(businessPhoto)).status()).toBe(403);
  expect((await context.request.delete(`/api/creator/photo/${creatorId}`, { headers: { "X-Weymela-Request": "1" } })).status()).toBe(404);
  await login(context, "creator");
  const media = await context.request.get("/api/creator/photo");
  expect(media.status()).toBe(200);
  expect(media.headers()["cache-control"]).toBe("private, no-store");
  expect(await media.body()).toEqual(jpeg);
  const profile = await context.request.get("/api/profile");
  expect(await profile.text()).not.toMatch(/CreatorPhotoKey|\/var\/lib|v3-browser-creator-photos/i);
  await open(page, "/profile");
  await page.getByRole("button", { name: "Remove photo" }).click();
  await expect(page.getByRole("button", { name: "Add photo" })).toBeVisible();
  await expect(page.locator(".profile-avatar img")).toHaveCount(0);
  await expect(page.locator(".profile-avatar")).toHaveText("B");
  await login(context, "business");
  expect((await context.request.get(businessPhoto)).status()).toBe(404);
  await open(page, `/business/campaigns/${creatorCampaign}?tab=applicants`);
  const fallback = applicants.locator(".person:visible").filter({ hasText: "Bella" }).locator(".creator-photo-avatar");
  await expect(fallback).toHaveText("B");
  await expect(fallback.locator("img")).toHaveCount(0);
  expect(imageViolations).toEqual([]);
});
