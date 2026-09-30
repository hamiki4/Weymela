import { expect, test } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";

for (const width of [320, 360, 375, 390, 393, 430]) {
  test(`Business UGC posting and Creator slots at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    await login(context, "business");
    await open(page, "/business/ugc/new");
    await expect(page.getByRole("radio", { name: "Deliver content only" })).toBeChecked();
    await expect(page.getByLabel("Creator capacity")).toBeVisible();
    await expect(page.getByRole("button", { name: "Add TikTok Creator slot" })).toHaveCount(0);

    await page.getByRole("radio", { name: "Creator must post" }).check();
    await expect(page.getByLabel("Creator capacity")).toHaveCount(0);
    await page.getByRole("button", { name: "Add TikTok Creator slot" }).click();
    await page.getByRole("button", { name: "Add TikTok Creator slot" }).click();
    await page.getByRole("button", { name: "Add Instagram Creator slot" }).click();
    await expect(page.getByLabel("TikTok Creator slots")).toHaveText("2");
    await expect(page.getByLabel("Instagram Creator slots")).toHaveText("1");
    await expect(page.getByLabel("YouTube Creator slots")).toHaveText("0");

    const title = `UGC slots ${width}-${Date.now()}`;
    await page.getByLabel("UGC title").fill(title);
    await page.getByLabel("Creator payment").fill("500");
    await page.getByLabel("Due date").fill(new Date(Date.now() + 14 * 86400000).toISOString().slice(0, 16));
    await page.getByLabel("Instructions").fill("Make a short product video.");
    await page.getByRole("radio", { name: /Product provided by Business/ }).check();
    await layout(page);
    if (width === 390 || width === 393) await screenshot(page, `${width}-business-ugc-platform-capacity`);
    await page.getByRole("button", { name: "Save UGC Draft" }).click();
    await expect(page).toHaveURL(/\/business\/ugc$/);

    const response = await context.request.get("/api/business/ugc", { headers: { "X-Weymela-Request": "1" } });
    expect(response.ok()).toBeTruthy();
    const rows = await response.json() as { title: string; creatorsNeeded: number;
      platformCapacities: { platform: string; capacity: number; approved: number; available: number }[];
      platformRequirements: { platform: string }[] }[];
    const created = rows.find(row => row.title === title);
    expect(created).toBeDefined();
    expect(created!.creatorsNeeded).toBe(3);
    expect(created!.platformCapacities.map(row => [row.platform, row.capacity, row.approved, row.available]).sort()).toEqual([
      ["Instagram", 1, 0, 1],
      ["TikTok", 2, 0, 2],
    ]);
    expect(created!.platformRequirements.map(row => row.platform).sort()).toEqual(["Instagram", "TikTok"]);
  });
}
