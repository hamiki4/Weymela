import { expect, test } from "@playwright/test";
import { layout, login, open } from "./helpers";

const anti = {
  id: "59b49723-1c3a-4b8f-a51d-d1f2f959d1de",
  type: "AntiCircumventionAgreement",
  version: "initial-2026-09-28.1",
  contentHash: "sha256:05a297468a6a513bc604a21c0e58a39d06c60ae8bdee8d3462a968d619f4078e",
};

async function legalRoutes(page: import("@playwright/test").Page) {
  let accepted = false;
  let writes = 0;
  await page.route("**/api/legal/current", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify([
    { id: "00000000-0000-4000-8000-000000000401", type: "CreatorAgreement", version: "fixture", contentHash: "fixture", accepted: true },
    { ...anti, accepted },
  ]) }));
  await page.route("**/api/legal/*/content", route => route.fulfill({ status: 200, contentType: "application/json",
    body: JSON.stringify({ ...anti, content: "Do not provide false information. Browser fixture only." }) }));
  await page.route("**/api/legal/*/accept", async route => {
    writes += 1;
    accepted = true;
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ id: anti.id }) });
  });
  return { writes: () => writes };
}

for (const width of [320, 360, 375, 390, 430]) {
  test(`Creator legal acceptance remains usable at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    await login(context, "creator");
    const state = await legalRoutes(page);
    await page.goto(`/creator/legal?returnTo=${encodeURIComponent("/creator/promotions")}`);
    await expect(page.getByRole("heading", { name: "Before you continue" })).toBeVisible();
    const checkbox = page.getByRole("checkbox", { name: "I agree to Weymela's rules and regulations." });
    const accept = page.getByRole("button", { name: "Accept & Continue" });
    await expect(checkbox).not.toBeChecked();
    await expect(accept).toBeDisabled();
    await page.getByRole("button", { name: "rules and regulations" }).click();
    const viewer = page.getByRole("dialog", { name: "Weymela rules and regulations" });
    await expect(viewer.getByText("Do not provide false information. Browser fixture only.")).toBeVisible();
    expect(state.writes()).toBe(0);
    await viewer.getByRole("button", { name: "Back" }).click();
    await checkbox.check();
    await expect(accept).toBeEnabled();
    await layout(page);
    const bounds = await accept.boundingBox();
    expect(bounds).not.toBeNull();
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width + 1);
    await accept.click();
    await expect(page).toHaveURL(/\/creator\/promotions$/);
    await expect(page.getByRole("heading", { name: "My Promotions" })).toBeVisible();
    expect(state.writes()).toBe(1);
    await page.goto("/creator/legal?returnTo=%2Fcreator%2Fpromotions");
    await expect(page).toHaveURL(/\/creator\/promotions$/);
    expect(state.writes()).toBe(1);
  });
}

test("Creator Promotion request returns to its original detail page after acceptance", async ({ page, context }) => {
  // Own this eligible Promotion: other BrowserHost tests may fill every slot in
  // their Promotions, and a focused run starts with no published Promotion.
  await login(context, "business");
  await open(page, "/business/campaigns/new");
  const title = `Legal return Promotion ${Date.now()}`;
  await page.getByLabel("Promotion title", { exact: true }).fill(title);
  await page.getByLabel("Promotion type", { exact: true }).selectOption("ViewPlusCommission");
  await page.getByLabel("Promotion ends", { exact: true })
    .fill(new Date(Date.now() + 7 * 86400000).toISOString().slice(0, 10));
  await page.getByRole("button", { name: "Continue", exact: true }).click();
  await page.getByLabel("Requirements", { exact: true }).fill("One original video.");
  await page.getByLabel("Creator category", { exact: true }).fill("Food");
  await page.getByLabel("Region", { exact: true }).fill("Addis Ababa");
  await page.getByRole("button", { name: "Add TikTok Creator slot" }).click();
  await page.getByRole("button", { name: "Continue", exact: true }).click();
  await page.getByLabel("Promotion budget", { exact: true }).fill("1000");
  await page.getByRole("button", { name: "Create Draft" }).click();
  await expect(page.getByRole("heading", { name: title, exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Review Funding" }).click();
  await page.getByRole("button", { name: "Confirm & Reserve Funds" }).click();
  await page.getByRole("button", { name: "Publish Promotion" }).click();
  await expect(page.getByText("Promotion published. Eligible Creators can now find it.", { exact: true })).toBeVisible();

  await login(context, "creator");
  const state = await legalRoutes(page);
  await open(page, "/creator/discover");
  const opportunity = page.locator(".creator-opportunity-card")
    .filter({ has: page.getByRole("heading", { name: title, exact: true }) });
  await opportunity.getByRole("link", { name: "Request to Join" }).click();
  await expect(page.getByRole("heading", { name: "Before you continue" })).toBeVisible();
  const returnTo = new URL(page.url()).searchParams.get("returnTo");
  expect(returnTo).toMatch(/^\/creator\/discover\/[0-9a-f-]{36}$/i);
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Accept & Continue" }).click();
  await expect(page).toHaveURL(new RegExp(`${returnTo}$`));
  await expect(page.getByRole("button", { name: "Submit Request" })).toBeVisible();
  expect(state.writes()).toBe(1);
});
