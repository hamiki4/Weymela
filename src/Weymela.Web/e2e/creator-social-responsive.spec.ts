import { expect, test } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";

for (const width of [320, 360, 375, 390, 430]) {
  test(`Creator social profiles remain compact at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    await page.route("**/api/account/security", route => route.fulfill({ json: { passwordEnrolled: true, phoneEnrolled: true } }));
    await page.route("**/api/profile", route => route.fulfill({ json: {
      role: "Creator", displayName: "Bella", publicId: "CR-BFB1200263DE37F79F1C4B0EA5D61A9D", creatorId: 7205,
      email: "bella@example.test", phone: null, status: "Active", businessType: null, region: null,
    } }));
    const profiles: { id: string; platform: string; profileUrl: string }[] = [];
    await page.route("**/api/creator/social-accounts", route => route.fulfill({ json: profiles }));
    await page.route("**/api/creator/social-profiles/*", route => {
      const platform = route.request().url().split("/").at(-1)!;
      if (route.request().method() === "POST") {
        const { profileUrl } = route.request().postDataJSON() as { profileUrl: string };
        const index = profiles.findIndex(row => row.platform === platform);
        const next = { id: platform, platform, profileUrl };
        if (index < 0) profiles.push(next); else profiles[index] = next;
        return route.fulfill({ json: { id: platform } });
      }
      const index = profiles.findIndex(row => row.platform === platform);
      if (index >= 0) profiles.splice(index, 1);
      return route.fulfill({ status: 204, body: "" });
    });
    await login(context, "creator");
    await open(page, "/profile");
    await expect(page.getByRole("heading", { name: "Social Profiles" })).toBeVisible();
    await expect(page.getByText("Creator ID 7205")).toBeVisible();
    await expect(page.getByText("CR-BFB1200263DE37F79F1C4B0EA5D61A9D")).toHaveCount(0);
    await expect(page.getByText("Public ID")).toHaveCount(0);
    await expect(page.getByRole("navigation", { name: "Mobile navigation" })).toBeVisible();
    await expect(page.locator(".creator-social-row")).toHaveCount(4);
    await layout(page);

    const tiktok = page.locator(".creator-social-row").filter({ hasText: "TikTok" });
    await tiktok.getByRole("button", { name: "Add profile" }).click();
    const dialog = page.getByRole("dialog", { name: "TikTok profile" });
    await dialog.getByLabel("TikTok profile URL").fill("https://www.tiktok.com/@bella");
    await layout(page);
    await dialog.getByRole("button", { name: "Save" }).click();
    await expect(tiktok.getByText("@bella")).toBeVisible();
    await expect(tiktok.getByRole("link", { name: "View TikTok profile" })).toHaveAttribute("href", "https://www.tiktok.com/@bella");
    await tiktok.getByRole("button", { name: "Edit" }).click();
    await dialog.getByLabel("TikTok profile URL").fill("https://www.tiktok.com/@bella_new");
    await dialog.getByRole("button", { name: "Save" }).click();
    await expect(tiktok.getByText("@bella_new")).toBeVisible();
    await tiktok.getByRole("button", { name: "Edit" }).click();
    await dialog.getByRole("button", { name: "Remove" }).click();
    await expect(tiktok.getByText("Not added")).toBeVisible();

    const youtube = page.locator(".creator-social-row").filter({ hasText: "YouTube" });
    await youtube.getByRole("button", { name: "Add profile" }).click();
    const longHandle = `@${"a".repeat(95)}`;
    await page.getByRole("dialog", { name: "YouTube profile" }).getByLabel("YouTube profile URL")
      .fill(`https://www.youtube.com/${longHandle}`);
    await layout(page);
    await page.getByRole("dialog", { name: "YouTube profile" }).getByRole("button", { name: "Save" }).click();
    await expect(youtube.getByText(longHandle)).toBeVisible();
    await layout(page);
    await expect(page.getByRole("navigation", { name: "Mobile navigation" })).toBeVisible();
    if (width === 390) await screenshot(page, "creator-social-profile-390");
  });
}
