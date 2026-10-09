import { test, expect } from "@playwright/test";
import { layout, login, open, saveFundPostAndOpenPromotion, screenshot } from "./helpers";

for (const width of [375, 1366])
  test(`real Promotion lifecycle through all three roles at ${width}px`, async ({
    page,
    context,
  }) => {
    await page.setViewportSize({ width, height: width < 600 ? 812 : 768 });
    await login(context, "business");
    await page.goto("/business/campaigns/new?type=views-sales");
    await expect(page.getByRole("heading", { name: "Create Promotion" })).toBeVisible();
    const title = `Local stories ${width}-${Date.now()}`;
    await page.getByLabel("Promotion title", { exact: true }).fill(title);
    const closes = new Date(Date.now() + 3 * 86400000).toISOString().slice(0, 10);
    const due = new Date(Date.now() + 7 * 86400000).toISOString().slice(0, 10);
    await page.getByLabel("Application closes", { exact: true }).fill(closes);
    await page.getByLabel("Content due", { exact: true }).fill(due);
    await page.getByLabel("Region", { exact: true }).fill("Addis Ababa");
    await page.getByRole("button", { name: "Add TikTok Creator slot" }).click();
    await page.getByLabel("Minimum followers", { exact: true }).first().fill("1000");
    await layout(page);
    await screenshot(page, `${width}-flow-creator-requirements`);
    await page.getByLabel("Promotion budget", { exact: true }).fill("1000");
    await screenshot(page, `${width}-flow-campaign-budget`);
    const campaignId = await saveFundPostAndOpenPromotion(page);
    try {
      await expect(
        page.getByRole("heading", { name: title, exact: true }),
      ).toBeVisible();
    } catch (error) {
      const notices = await page.locator(".notice.error").allTextContents();
      throw new Error(
        `Promotion publish did not navigate. url=${page.url()} errors=${JSON.stringify(notices)} original=${String(error)}`,
      );
    }
    await login(context, "other-creator");
    await open(page, "/creator/discover");
    const card = page
      .locator("article")
      .filter({ has: page.getByRole("heading", { name: title, exact: true }) });
    await card.getByRole("link", { name: "Request to Join" }).click();
    await page.getByRole("radiogroup", { name: "Choose platform" })
      .getByRole("radio", { name: "https://www.tiktok.com/@elias" }).check();
    await page
      .getByLabel("Short message", { exact: true })
      .fill("I would love to create this.");
    await page
      .getByLabel("Content concept (optional)", { exact: true })
      .fill("A warm neighbourhood story.");
    await screenshot(page, `${width}-flow-creator-request`);
    await page.getByRole("button", { name: "Submit Request" }).click();
    await expect(page.getByText(/Request pending/)).toBeVisible();
    await login(context, "business");
    await open(page, `/business/campaigns/${campaignId}?tab=applicants`);
    await page.getByRole("button", { name: "Approve", exact: true }).click();
    await page.getByLabel("Creator Budget", { exact: true }).fill("500");
    await screenshot(page, `${width}-flow-approve-budget`);
    await page.getByRole("button", { name: "Approve & Set Budget" }).click();
    await expect(
      page.getByText("Creator approved and Creator Budget saved.", {
        exact: true,
      }),
    ).toBeVisible();
    await page
      .getByRole("tab", { name: "Approved Creators", exact: true })
      .click();
    await page
      .getByRole("button", { name: "Increase Budget", exact: true })
      .click();
    await page.getByLabel("Amount to add", { exact: true }).fill("100");
    await page.getByRole("button", { name: "Confirm Increase" }).click();
    await expect(
      page.getByText("Creator Budget increased.", { exact: true }),
    ).toBeVisible();
    await screenshot(page, `${width}-flow-budget-saved`);
    await login(context, "other-creator");
    await open(page, "/creator/promotions");
    await page
      .locator("article")
      .filter({ has: page.getByRole("heading", { name: title, exact: true }) })
      .getByRole("link", { name: "Add Content" })
      .click();
    const creatorBudgetId = page.url().split("/").pop()!;
    await page
      .getByLabel("TikTok Video Link", { exact: true })
      .fill("https://www.tiktok.com/@weymela.creator/video/7412345678901234567");
    await page.getByRole("button", { name: "Submit for Review", exact: true }).click();
    await expect(page.getByText(/under Business review/i)).toBeVisible();
    await expect(page.getByRole("button", { name: "Refresh Views" })).toHaveCount(0);
    await login(context, "business");
    await open(page, `/business/campaigns/${campaignId}`);
    const review = page.locator(".business-promotion-review-row").filter({ hasText: title });
    await review
      .getByLabel("Feedback (required for changes requested)")
      .fill("Please revise the opening.");
    await review.getByRole("button", { name: "Request Changes", exact: true }).click();
    await expect(review.getByText("Changes Requested", { exact: true })).toBeVisible();
    await login(context, "other-creator");
    await open(page, "/creator/promotions");
    const changesRequested = page.locator("article").filter({ has: page.getByRole("heading", { name: title, exact: true }) });
    await changesRequested.getByRole("link", { name: "Update Content", exact: true }).click();
    await page
      .getByLabel("TikTok Video Link", { exact: true })
      .fill("https://www.tiktok.com/@weymela.creator/video/7412345678901234568");
    await page.getByRole("button", { name: "Submit Revised Content", exact: true }).click();
    await expect(page.getByText(/under Business review/i)).toBeVisible();
    await login(context, "business");
    await open(page, `/business/campaigns/${campaignId}`);
    const revisedReview = page
      .locator(".business-promotion-review-row")
      .filter({ hasText: title })
      .filter({ hasText: "Revision 2" });
    await revisedReview.getByRole("button", { name: "Approve", exact: true }).click();
    await expect(revisedReview.getByText("Approved")).toBeVisible();
    await login(context, "other-creator");
    await open(page, `/creator/promotions/${creatorBudgetId}`);
    await page
      .getByLabel("TikTok Video Link", { exact: true })
      .fill(`https://www.tiktok.com/@weymela.creator/video/${Date.now()}${width}`);
    await page.getByRole("button", { name: "Verify Publication", exact: true }).click();
    await expect(page.getByRole("button", { name: "Go Live", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Go Live", exact: true }).click();
    await expect(
      page.getByRole("button", { name: "Refresh Views" }),
    ).toBeVisible();
    await expect(
      page.locator("main .badge").filter({ hasText: "Active" }).first(),
    ).toBeVisible();
    await layout(page);
    await screenshot(page, `${width}-flow-creator-active`);
    await login(context, "admin");
    await open(page, `/admin/campaigns/${campaignId}`);
    await expect(
      page.getByText("Financial summary", { exact: true }),
    ).toBeVisible();
    await expect(page.locator("main")).toContainText("Elias");
    await screenshot(page, `${width}-flow-admin-oversight`);
  });

test("real Add Funds accepts an arbitrary positive amount", async ({
  page,
  context,
}) => {
  await login(context, "business");
  await open(page, "/business/wallet");
  const before = await (
    await context.request.get("/api/business/wallet")
  ).json();
  await expect(page.getByLabel("Amount", { exact: true })).toBeVisible();
  await expect(page.getByLabel("Amount", { exact: true })).toBeVisible();
  await page.getByLabel("Amount", { exact: true }).fill("17.23");
  await page.getByRole("button", { name: "Add Funds", exact: true }).click();
  await expect(
    page.getByText("Funds added. Your saved wallet balance is shown above.", {
      exact: true,
    }),
  ).toBeVisible();
  const after = await (
    await context.request.get("/api/business/wallet")
  ).json();
  expect(after.available).toBeCloseTo(before.available + 17.23, 2);
  expect(after.reserved).toBe(before.reserved);
});
