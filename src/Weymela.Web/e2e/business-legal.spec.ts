import { expect, test } from "@playwright/test";
import { layout, login, screenshot } from "./helpers";

const documents = [
  { id: "00000000-0000-0000-0000-000000000201", type: "BusinessAgreement", version: "review-2", contentHash: "sha256:business", accepted: false },
  { id: "00000000-0000-0000-0000-000000000301", type: "AntiCircumventionAgreement", version: "review-3", contentHash: "sha256:rules", accepted: false },
];

for (const width of [320, 360, 375, 390, 430]) {
  test(`Business legal resolution remains usable at ${width}px without approved content`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    await login(context, "business");
    await page.route("**/api/legal/current", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(documents) }));
    await page.route("**/api/legal/*/content", route => route.fulfill({ status: 503, contentType: "application/json", body: '{"message":"Approved content unavailable"}' }));
    for (const destination of ["/business/campaigns/new", "/business/ugc/new"]) {
      await page.goto(destination);
      await expect(page).toHaveURL(/\/business\/legal\?/);
      expect(new URL(page.url()).searchParams.get("returnTo")).toBe(destination);
      await expect(page.getByRole("heading", { name: "Before you continue" })).toBeVisible();
      const checkbox = page.getByRole("checkbox");
      const accept = page.getByRole("button", { name: "Accept & Continue" });
      await expect(checkbox).not.toBeChecked();
      await expect(accept).toBeDisabled();
      await page.getByRole("button", { name: "Business Terms" }).click();
      const terms = page.getByRole("dialog", { name: "Business Terms" });
      await expect(terms.getByText("Version review-2")).toBeVisible();
      await expect(terms.getByText("Approved text for this version is unavailable.")).toBeVisible();
      if (width === 390 && destination.endsWith("campaigns/new")) await screenshot(page, "business-legal-document-390");
      await terms.getByRole("button", { name: "Back" }).click();
      await page.getByRole("button", { name: "Anti-Circumvention Rules" }).click();
      const rules = page.getByRole("dialog", { name: "Anti-Circumvention Rules" });
      await expect(rules.getByText("Version review-3")).toBeVisible();
      await rules.getByRole("button", { name: "Back" }).click();
      await checkbox.check();
      await expect(accept).toBeDisabled();
      await layout(page);
      if (width === 390) await screenshot(page, destination.endsWith("ugc/new") ? "business-legal-ugc-390" : "business-legal-promotion-390");
      const bounds = await accept.boundingBox();
      expect(bounds).not.toBeNull();
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width + 1);
    }
  });
}
