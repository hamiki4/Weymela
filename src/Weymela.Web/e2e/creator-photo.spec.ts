import { expect, test } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";

const png = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAEklEQVR4AWIqjpn8H4SZGKAAAAAA//+9j9SYAAAABklEQVQDAD6oBMcQkXozAAAAAElFTkSuQmCC", "base64");

test("Creator photo changes on Profile and is visible only through Business Creator review", async ({ page, context }) => {
  await login(context, "creator");
  await open(page, "/profile");
  await expect(page.getByRole("button", { name: "Add photo" })).toBeVisible();
  await page.getByLabel("Choose Creator profile photo").setInputFiles({ name: "creator.png", mimeType: "image/png", buffer: png });
  await expect(page.getByAltText("Selected profile photo preview")).toBeVisible();
  await page.getByRole("button", { name: "Save photo" }).click();
  await expect(page.getByText("Photo updated.")).toBeVisible();
  await expect(page.locator(".profile-avatar img")).toBeVisible();
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
  await open(page, "/creator/discover?tab=UGC");
  const opportunity = page.locator(".creator-opportunity-card").filter({ hasText: title });
  const joined = page.waitForResponse(response => response.request().method() === "POST" && response.url().includes(`/api/creator/ugc/${ugcId}/request`));
  await opportunity.getByRole("button", { name: "Request to Join" }).click();
  expect((await joined).ok()).toBeTruthy();
  await login(context, "business");
  await open(page, "/business/ugc");
  const ugcCard = page.locator(".data-card").filter({ hasText: title });
  await ugcCard.getByRole("button", { name: "Creator requests & assignments" }).click();
  await expect(ugcCard.locator(".business-ugc-creator-row .creator-photo-avatar img")).toBeVisible();
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
  await page.getByRole("button", { name: "Remove photo" }).click();
  await expect(page.getByRole("button", { name: "Add photo" })).toBeVisible();
  await expect(page.locator(".profile-avatar img")).toHaveCount(0);
});
