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
    // A fresh BrowserHost has no Creator-discoverable Promotion. Reuse this
    // fixture across viewport cases that share one disposable database.
    await login(context, "business");
    const walletState = await (await context.request.get("/api/business/wallet")).json() as { available: number; version: number };
    if (walletState.available < 1000) {
      const funded = await context.request.post("/api/business/wallet/deposits", {
        headers: { "X-Weymela-Request": "1", "Idempotency-Key": `responsive-funding-${viewport.width}` },
        data: { amount: 2000, expectedVersion: walletState.version },
      });
      expect(funded.status()).toBe(204);
    }
    const responsiveOpportunityTitle = "Responsive Creator detail fixture";
    const businessPromotions = await (await context.request.get("/api/business/campaigns"))
      .json() as { title: string; status: string }[];
    if (!businessPromotions.some(item => item.title === responsiveOpportunityTitle && item.status === "Active")) {
      await page.goto("/business/campaigns/new");
      await expect(page.getByRole("heading", { name: "Create Promotion" })).toBeVisible();
      await page.getByLabel("Promotion title", { exact: true }).fill(responsiveOpportunityTitle);
      await page.getByLabel("Promotion type", { exact: true }).selectOption("ViewPlusCommission");
      await page.getByLabel("Application closes", { exact: true })
        .fill(new Date(Date.now() + 3 * 86400000).toISOString().slice(0, 16));
      await page.getByLabel("Content due", { exact: true })
        .fill(new Date(Date.now() + 7 * 86400000).toISOString().slice(0, 16));
      await page.getByLabel("Requirements", { exact: true }).fill("One original video.");
      await page.getByLabel("Creator category", { exact: true }).fill("Food");
      await page.getByLabel("Region", { exact: true }).fill("Addis Ababa");
      await page.getByRole("button", { name: "Add TikTok Creator slot" }).click();
      await page.getByLabel("Promotion budget", { exact: true }).fill("1000");
      await page.getByRole("button", { name: "Publish Promotion" }).click();
      await expect(page).toHaveURL(/\/business\/campaigns\/[0-9a-f-]{36}$/i);
    }
    const screens: [string, string[]][] = [
      [
        "business",
        [
          "/business",
          "/business/wallet",
          "/business/transactions",
          "/business/campaigns/new",
          "/business/campaigns",
          "/business/requests",
          "/business/ugc",
          "/business/ugc/new",
          "/business/pricing",
          "/business/cashiers",
          "/profile",
        ],
      ],
      [
        "creator",
        [
          "/creator",
          "/creator/discover",
          "/creator/promotions",
          "/creator/earnings",
          "/profile",
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
      ["customer", ["/customer/offers", "/customer/discover", "/customer/transactions", "/customer/cashback", "/profile"]],
      ["cashier", ["/checkout", "/checkout/transactions"]],
    ];
    for (const [role, paths] of screens) {
      await login(context, role);
      for (const path of paths) {
        if (role === "business" && path === "/business/campaigns/new") {
          await page.goto(path);
          await expect(page.getByRole("heading", { name: "Create Promotion" })).toBeVisible();
        } else if (role === "creator" && path === "/creator/earnings") {
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
            role === "business" ? 4 : 2,
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
        if (viewport.width <= 430 && role === "business" && path === "/business/campaigns/new") {
          const available = (await (await context.request.get("/api/business/wallet")).json() as { available: number }).available;
          await page.getByLabel("Promotion title").fill(`Responsive funding ${viewport.width}`);
          await page.getByLabel("Application closes").fill(new Date(Date.now() + 3 * 86400000).toISOString().slice(0, 16));
          await page.getByLabel("Content due").fill(new Date(Date.now() + 7 * 86400000).toISOString().slice(0, 16));
          if (viewport.width === 390 || viewport.width === 393)
            await screenshot(page, `${viewport.width}-business-promotion-creators`);
          await page.getByRole("button", { name: "Add TikTok Creator slot" }).click();
          await page.getByLabel("Promotion budget").fill(String(available + 1000));
          await expect(page.getByText("1,000 ETB more", { exact: true })).toBeVisible();
          await expect(page.getByRole("link", { name: "Add Funds" })).toBeVisible();
          await layout(page);
          await screenshot(page, `${viewport.width}-business-promotion-shortfall`);
        }
        if (viewport.width <= 430 && role === "business" && path === "/business/ugc") {
          await expect(page.getByRole("heading", { name: "Your UGC" })).toBeVisible();
          await expect(page.getByRole("link", { name: "Create UGC" })).toHaveAttribute("href", "/business/ugc/new");
          await expect(page.getByLabel("UGC title")).toHaveCount(0);
        }
        if (viewport.width <= 430 && role === "business" && path === "/business/ugc/new") {
          const available = (await (await context.request.get("/api/business/wallet")).json() as { available: number }).available;
          await expect(page.getByRole("heading", { name: "Create UGC", exact: true })).toBeVisible();
          const summary = page.locator("section.panel").filter({ has: page.getByRole("heading", { name: "Funding Summary" }) });
          await page.getByLabel("Creator payment").fill(String(available + 1000));
          await expect(summary.getByText("Need", { exact: true })).toBeVisible();
          await expect(summary.getByRole("link", { name: "Add Funds" })).toBeVisible();
          await layout(page);
          await screenshot(page, `${viewport.width}-business-ugc-shortfall`);
        }
      }
      if (viewport.width <= 430 && ["customer", "creator", "business", "cashier"].includes(role)) {
        if (role === "cashier") {
          await page.getByRole("button", { name: "Open account menu" }).click();
          await expect(page.getByRole("dialog", { name: "Account menu" })).toBeVisible();
          await screenshot(page, `${viewport.width}-${role}-account-menu`);
          await page.keyboard.press("Escape");
        } else {
          await page.getByRole("button", { name: "Open Settings" }).click();
          const settings = page.getByRole("dialog", { name: "Settings" });
          await expect(settings.getByLabel("Switch profile")).toBeVisible();
          await expect(settings.getByRole("button", { name: "Notifications" })).toBeVisible();
          await expect(settings.getByRole("button", { name: "Location" })).toBeVisible();
          if (role === "creator") await expect(settings.getByRole("link", { name: "Social Profiles" })).toBeVisible();
          if (role === "business") await expect(settings.getByRole("link", { name: "Cashier Management" })).toBeVisible();
          await layout(page);
          await screenshot(page, `${viewport.width}-${role}-settings`);
          await page.keyboard.press("Escape");
          await expect(settings).not.toBeVisible();
          await page.getByRole("navigation", { name: "Mobile navigation" }).getByRole("link", { name: "Profile" }).click();
          await expect(page).toHaveURL(/\/profile$/);
          await expect(page.getByRole("heading", { name: "Profile", exact: true })).toBeVisible();
          if (role === "creator") await expect(page.getByRole("heading", { name: "Social Profiles" })).toBeVisible();
          if (role === "business") await expect(page.getByRole("heading", { name: "Business Information" })).toBeVisible();
          await layout(page);
          await screenshot(page, `${viewport.width}-${role}-profile`);
          const navBounds = await page.getByRole("navigation", { name: "Mobile navigation" }).boundingBox();
          expect(navBounds).not.toBeNull();
          expect(navBounds!.y + navBounds!.height).toBeLessThanOrEqual(viewport.height + 1);
        }
        const clippedLabels = await page.getByRole("navigation", { name: "Mobile navigation" }).locator(".mobile-role-link").evaluateAll(links => links.flatMap(link => {
          const label = link.querySelector("span");
          if (!label) return [];
          const text = label.getBoundingClientRect();
          const target = link.getBoundingClientRect();
          return label.scrollWidth > label.clientWidth + 1 || text.left < target.left - 1 || text.right > target.right + 1
            ? [label.textContent?.trim() ?? "unknown"] : [];
        }));
        expect(clippedLabels, `${role} bottom-navigation labels at ${viewport.width}px`).toEqual([]);
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
        ).json() as { id: string; title: string }[];
        const opportunity = opportunities.find(item => item.title === responsiveOpportunityTitle);
        expect(opportunity, "published Creator detail fixture must be discoverable").toBeDefined();
        await open(page, `/creator/discover/${opportunity!.id}`);
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
    hoverMotionRules: Array.from(document.styleSheets).filter((sheet) => {
      try { return Array.from(sheet.cssRules).some((rule) => rule.cssText.includes(":hover")
        && /(?:transform|scale|translate)\s*:/.test(rule.cssText)); } catch { return false; }
    }).length,
  }));
  expect(typography.root).toBeGreaterThanOrEqual(17);
  expect(typography.bottomLabel).toBeGreaterThanOrEqual(12);
  expect(typography.overflow).toBe(false);
  expect(typography.hoverMotionRules).toBe(0);
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
  await page.getByRole("button", { name: "Open Settings" }).click();
  await expect(
    page.getByRole("dialog", { name: "Settings" }),
  ).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(
    page.getByRole("dialog", { name: "Settings" }),
  ).not.toBeVisible();
  await expect(page.getByRole("button", { name: "Open Settings" })).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(
    page.getByRole("dialog", { name: "Settings" }),
  ).toBeVisible();
});

test("waiting update stays compact, safe and dismissible across mobile widths", async ({ page, context }) => {
  await login(context, "business");
  await open(page, "/business");
  await page.evaluate(() => {
    const scope = window as Window & { testUpdateActivations?: number };
    scope.testUpdateActivations = 0;
    window.dispatchEvent(new CustomEvent("weymela-update", {
      detail: { postMessage: () => { scope.testUpdateActivations = (scope.testUpdateActivations ?? 0) + 1; } },
    }));
  });
  const notice = page.getByRole("status", { name: "Update available" });
  for (const width of [320, 360, 375, 390, 430]) {
    await page.setViewportSize({ width, height: 844 });
    await expect(notice).toBeVisible();
    await expect(notice.getByRole("button", { name: "Reload" })).toBeVisible();
    await layout(page);
    const bounds = await notice.boundingBox();
    const nav = await page.getByRole("navigation", { name: "Mobile navigation" }).boundingBox();
    expect(bounds).not.toBeNull();
    expect(nav).not.toBeNull();
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width + 1);
    expect(bounds!.y + bounds!.height).toBeLessThan(nav!.y);
    if (width === 390) await screenshot(page, "390-business-update-notice");
  }
  expect(await page.evaluate(() => (window as Window & { testUpdateActivations?: number }).testUpdateActivations)).toBe(0);
  await page.getByRole("button", { name: "Open Settings" }).click();
  await expect(page.getByRole("dialog", { name: "Settings" })).toBeVisible();
  await page.keyboard.press("Escape");
  await notice.getByRole("button", { name: "Dismiss update notice" }).click();
  await expect(notice).not.toBeVisible();
  await page.getByRole("navigation", { name: "Mobile navigation" }).getByRole("link", { name: "Promotions" }).click();
  await expect(page).toHaveURL(/\/business\/campaigns$/);
  await expect(notice).not.toBeVisible();
});
