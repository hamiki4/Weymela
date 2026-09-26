import { test, expect } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";

const viewports = [
  { width: 320, height: 800 },
  { width: 360, height: 800 },
  { width: 375, height: 812 },
  { width: 390, height: 844 },
  { width: 393, height: 852 },
  { width: 430, height: 932 },
  { width: 768, height: 1024 },
  { width: 1366, height: 768 },
  { width: 1440, height: 900 },
  { width: 1920, height: 1080 },
];
for (const viewport of viewports)
  test(`rendered role workspaces at ${viewport.width}x${viewport.height}`, async ({
    page,
    context,
  }) => {
    test.setTimeout(240000);
    await page.setViewportSize(viewport);
    const screens: [string, string[]][] = [
      [
        "business",
        [
          "/business",
          "/business/wallet",
          "/business/campaigns/new",
          "/business/campaigns",
          "/business/requests",
          "/business/ugc",
          "/business/pricing",
        ],
      ],
      [
        "creator",
        [
          "/creator",
          "/creator/discover",
          "/creator/promotions",
          "/creator/earnings",
        ],
      ],
      [
        "admin",
        [
          "/admin",
          "/admin/customers",
          "/admin/customers/new",
          "/admin/campaigns",
          "/admin/wallets",
          "/admin/ugc",
          "/admin/reports",
          "/admin/settings",
          "/admin/payouts",
          "/admin/businesses",
          "/admin/businesses/new",
          "/admin/creators",
          "/admin/creators/new",
          "/admin/admins",
          "/admin/admins/new",
          "/notifications",
        ],
      ],
      ["customer", ["/customer/offers", "/customer/discover", "/customer/transactions", "/customer/cashback"]],
      ["cashier", ["/checkout", "/checkout/transactions"]],
    ];
    for (const [role, paths] of screens) {
      await login(context, role);
      for (const path of paths) {
        if (role === "creator" && path === "/creator/earnings") {
          await page.goto(path);
          await expect(
            page.getByRole("heading", { name: "Earnings", exact: true }),
          ).toBeVisible();
          await expect(
            page.getByRole("status", { name: "Loading workspace" }),
          ).toHaveCount(0);
          await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
          await expect(page.getByRole("alert")).toHaveCount(0);
        } else {
          await open(page, path);
        }
        await layout(page);
        await screenshot(
          page,
          `${viewport.width}-${path.slice(1).replaceAll("/", "-")}`,
        );
        if (role === "business") {
          await expect(page.locator("main")).not.toContainText(/NaN\s*ETB|NaN/i);
          await expect(
            page.getByRole("button", {
              name: /End Campaign|End Promotion|Decrease Budget/,
            }),
          ).toHaveCount(0);
          await expect(page.locator("main")).not.toContainText("Allocation");
        }
        if (path.endsWith("/pricing")) {
          await expect(
            page.getByRole("status", { name: "Loading workspace" }),
          ).toHaveCount(0);
          await expect(page.locator("main .empty-state")).toHaveCount(0);
          await expect(page.locator("main .pricing-card-grid")).toHaveCount(1);
          await expect(page.locator("main .pricing-card")).toHaveCount(
            role === "business" ? 3 : 2,
          );
          await expect(page.locator("main")).not.toContainText(
            /Customer Cashback|Platform Keeps|Platform revenue/,
          );
        }
        if (path === "/admin/payouts") {
          if (viewport.width <= 430) {
            await open(page, "/admin");
            await page.getByRole("navigation", { name: "Mobile navigation" }).getByRole("link", { name: "Payouts", exact: true }).click();
            await expect(page).toHaveURL(/\/admin\/payouts$/);
          }
          for (const tab of ["Customers", "Platform", "History"]) {
            const payoutTab = page
              .locator("main")
              .getByRole("tab", { name: tab, exact: true });
            await expect(payoutTab).toBeVisible();
            await payoutTab.click();
            await layout(page);
            await screenshot(
              page,
              `${viewport.width}-admin-payouts-${tab.toLowerCase()}`,
            );
          }
        }
      }
      if (viewport.width <= 430 && ["customer", "creator", "business", "cashier"].includes(role)) {
        await page.getByRole("button", { name: "Open account menu" }).click();
        await expect(page.getByRole("dialog", { name: "Account menu" })).toBeVisible();
        await screenshot(page, `${viewport.width}-${role}-profile-settings`);
        await page.keyboard.press("Escape");
      }
      if (role === "business" || role === "admin") {
        const campaigns = await (
          await context.request.get(`/api/${role}/campaigns`)
        ).json();
        const id =
          campaigns.find((r: { title: string }) => r.title.includes("coffee"))
            ?.id ?? campaigns[0].id;
        await open(page, `/${role}/campaigns/${id}`);
        await layout(page);
        await screenshot(page, `${viewport.width}-${role}-campaign-detail`);
        if (role === "business")
          for (const tab of ["applicants", "budgets", "funds", "performance"]) {
            await open(page, `/business/campaigns/${id}?tab=${tab}`);
            await layout(page);
            await screenshot(page, `${viewport.width}-business-${tab}`);
          }
      }
      if (role === "creator") {
        const rows = await (
          await context.request.get("/api/creator/campaigns")
        ).json();
        await open(page, `/creator/promotions/${rows[0].budgetId}`);
        await layout(page);
        await screenshot(page, `${viewport.width}-creator-active-detail`);
        const opportunities = await (
          await context.request.get("/api/creator/discover")
        ).json();
        await open(page, `/creator/discover/${opportunities[0].id}`);
        await layout(page);
        await screenshot(page, `${viewport.width}-creator-join-detail`);
      }
      if (role === "customer") {
        const rows = await (
          await context.request.get("/api/customer/offers")
        ).json();
        await open(page, `/customer/offers/${rows[0].id}`);
        await layout(page);
        await screenshot(page, `${viewport.width}-customer-offer`);
        await page
          .getByRole("button", { name: "Get Offer", exact: true })
          .click();
        await expect(
          page.getByRole("img", { name: "Offer QR for the cashier" }),
        ).toBeVisible();
        await layout(page);
        await screenshot(page, `${viewport.width}-customer-qr`);
        await expect(page.locator("main")).not.toContainText(
          /Creator promotion|selected creator|Shop this Offer/,
        );
        await page.getByRole("link", { name: "← Back", exact: true }).click();
        await expect(page).toHaveURL(/\/customer\/offers$/);
      }
    }
  });
test("stable buttons and mobile keyboard navigation", async ({
  page,
  context,
}) => {
  await login(context, "business");
  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, "/business");
  const typography = await page.evaluate(() => ({
    root: parseFloat(getComputedStyle(document.documentElement).fontSize),
    bottomLabel: parseFloat(getComputedStyle(document.querySelector<HTMLElement>(".mobile-role-link")!).fontSize),
    overflow: document.documentElement.scrollWidth > window.innerWidth + 1,
    decorativeHover: Array.from(document.styleSheets).filter((sheet) => {
      try { return Array.from(sheet.cssRules).some((rule) => rule.cssText.includes(":hover")); } catch { return false; }
    }).length,
  }));
  expect(typography.root).toBeGreaterThanOrEqual(17);
  expect(typography.bottomLabel).toBeGreaterThanOrEqual(12);
  expect(typography.overflow).toBe(false);
  expect(typography.decorativeHover).toBe(0);
  const button = page
    .getByRole("link", { name: "Create Promotion", exact: true })
    .first();
  const before = await button.evaluate((e) => ({
    transform: getComputedStyle(e).transform,
    width: e.getBoundingClientRect().width,
    color: getComputedStyle(e).backgroundColor,
  }));
  await button.hover();
  const after = await button.evaluate((e) => ({
    transform: getComputedStyle(e).transform,
    width: e.getBoundingClientRect().width,
    color: getComputedStyle(e).backgroundColor,
  }));
  expect(after.transform).toBe("none");
  expect(after.width).toBe(before.width);
  expect(after.color).not.toMatch(/128, 0, 128|147, 51, 234/);
  await page.keyboard.press("Tab");
  const skip = page.getByRole("link", { name: "Skip to content" });
  await expect(skip).toBeFocused();
  await screenshot(page, "390-keyboard-skip-link");
  await page.keyboard.press("Enter");
  await expect(page.locator("main")).toBeFocused();
  await open(page, "/business");
  await page.keyboard.press("Tab");
  await expect(skip).toBeFocused();
  await page.getByRole("button", { name: "Open account menu" }).click();
  await expect(
    page.getByRole("dialog", { name: "Account menu" }),
  ).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(
    page.getByRole("dialog", { name: "Account menu" }),
  ).not.toBeVisible();
  await expect(page.getByRole("button", { name: "Open account menu" })).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(
    page.getByRole("dialog", { name: "Account menu" }),
  ).toBeVisible();
});
