import { test, expect } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";

for (const width of [375, 1366])
  test(`real Campaign lifecycle through all three roles at ${width}px`, async ({
    page,
    context,
  }) => {
    await page.setViewportSize({ width, height: width < 600 ? 812 : 768 });
    await login(context, "business");
    await open(page, "/business/campaigns/new");
    const title = `Local stories ${width}-${Date.now()}`;
    await page.getByLabel("Campaign Title", { exact: true }).fill(title);
    await page
      .getByLabel("Description", { exact: true })
      .fill("An original story from our neighbourhood.");
    await page
      .getByLabel("Campaign Type", { exact: true })
      .selectOption("ViewPlusCommission");
    const start = new Date(Date.now() - 3600000).toISOString().slice(0, 16);
    const end = new Date(Date.now() + 7 * 86400000).toISOString().slice(0, 16);
    await page.getByLabel("Start date", { exact: true }).fill(start);
    await page.getByLabel("End date", { exact: true }).fill(end);
    await page.getByRole("button", { name: "Continue", exact: true }).click();
    await page
      .getByLabel("Requirements", { exact: true })
      .fill("One original video.");
    await page.getByLabel("Creator category", { exact: true }).fill("Food");
    await page.getByLabel("Region", { exact: true }).fill("Addis Ababa");
    await page
      .getByLabel("Minimum verified followers", { exact: true })
      .fill("1000");
    await layout(page);
    await screenshot(page, `${width}-flow-creator-requirements`);
    await page.getByRole("button", { name: "Continue", exact: true }).click();
    await page
      .getByLabel("Campaign Budget (ETB)", { exact: true })
      .fill("1000");
    await screenshot(page, `${width}-flow-campaign-budget`);
    await page.getByRole("button", { name: "Create Draft" }).click();
    await expect(
      page.getByRole("heading", { name: title, exact: true }),
    ).toBeVisible();
    const campaignId = page.url().split("/").pop()!;
    await page.getByRole("button", { name: "Review Funding" }).click();
    await expect(
      page.getByRole("dialog", { name: "Confirm Campaign Funding" }),
    ).toBeVisible();
    await layout(page);
    await screenshot(page, `${width}-flow-funding-confirmation`);
    await page.getByRole("button", { name: "Confirm & Reserve Funds" }).click();
    await expect(
      page.getByRole("button", { name: "Publish Campaign" }),
    ).toBeVisible();
    await page.getByRole("button", { name: "Publish Campaign" }).click();
    await expect(
      page.getByText("Campaign published. Eligible Creators can now find it.", {
        exact: true,
      }),
    ).toBeVisible();
    await login(context, "other-creator");
    await open(page, "/creator/discover");
    const card = page
      .locator("article")
      .filter({ has: page.getByRole("heading", { name: title, exact: true }) });
    await card.getByRole("link", { name: "Join Campaign" }).click();
    await page
      .getByLabel("Short message", { exact: true })
      .fill("I would love to create this.");
    await page
      .getByLabel("Content concept (optional)", { exact: true })
      .fill("A warm neighbourhood story.");
    await screenshot(page, `${width}-flow-creator-request`);
    await page.getByRole("button", { name: "Request to Join" }).click();
    await expect(page.getByText(/Your request is pending/)).toBeVisible();
    await login(context, "business");
    await open(page, `/business/campaigns/${campaignId}?tab=applicants`);
    await page.getByRole("button", { name: "Approve", exact: true }).click();
    await page.getByLabel("Creator Budget (ETB)", { exact: true }).fill("500");
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
    await page.getByLabel("Amount to add (ETB)", { exact: true }).fill("100");
    await page.getByRole("button", { name: "Confirm Increase" }).click();
    await expect(
      page.getByText("Creator Budget increased.", { exact: true }),
    ).toBeVisible();
    await screenshot(page, `${width}-flow-budget-saved`);
    await login(context, "other-creator");
    await open(page, "/creator/campaigns");
    await page
      .locator("article")
      .filter({ has: page.getByRole("heading", { name: title, exact: true }) })
      .getByRole("link", { name: "Open Campaign" })
      .click();
    await expect(page.getByText("600", { exact: true }).first()).toBeVisible();
    await page
      .getByLabel("Video reference", { exact: true })
      .fill(`${Date.now()}${width}`);
    await page.getByRole("button", { name: "Connect Content" }).click();
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
  await page.getByLabel("Amount (ETB)", { exact: true }).fill("17.23");
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
