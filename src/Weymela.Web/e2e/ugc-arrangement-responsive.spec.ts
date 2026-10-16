import { expect, test } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";

const opportunity = {
  id: "00000000-0000-0000-0000-000000000699", businessId: "biz", business: "Abc Coffee",
  title: "Product story", slogan: null, contentType: "Video", status: "Open",
  creatorPayment: 500, creatorsNeeded: 1, approvedCreators: 0, requiredFunding: 550,
  reservedFunding: 550, usedFunding: 0, dueDateUtc: "2027-12-01T10:00:00Z",
  location: null, platformRequirements: [], requestStatus: null, version: 1,
  productProvided: false, creatorMustPurchase: true,
};

for (const width of [320, 360, 375, 390, 393, 430]) {
  test(`UGC arrangement and shared icon readability at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    await page.route("**/api/business/promotion-management", route => route.fulfill({ json: [{
      key: "ugc-" + opportunity.id, id: opportunity.id, campaignId: opportunity.id, kind: "UGC campaign",
      title: opportunity.title, group: "Active", status: "Open", budget: 550, remaining: 550,
      dueAtUtc: opportunity.dueDateUtc, primaryAction: "Manage campaign", actionPath: `/business/ugc/${opportunity.id}`,
      creator: null, remainingDays: null,
    }] }));
    await page.route("**/api/business/ugc", route => route.fulfill({ json: [opportunity] }));
    await page.route("**/api/creator/ugc", route => route.fulfill({ json: [opportunity] }));
    await page.route(`**/api/business/ugc/${opportunity.id}`, route => route.fulfill({ json: {
      opportunity,
      instructions: "",
      resources: [],
      productProvided: false,
      creatorMustPurchase: true,
      usageRights: null,
      currentRevision: 1,
      requests: [],
      assignments: [],
      revisions: [],
    } }));
    await page.route(`**/api/creator/ugc/${opportunity.id}`, route => route.fulfill({ json: {
      opportunity,
      instructions: "",
      resources: [],
      productProvided: false,
      creatorMustPurchase: true,
      usageRights: null,
      currentRevision: 1,
      requests: [],
      assignments: [],
      revisions: [],
    } }));

    await login(context, "business");
    await open(page, "/business/campaigns/new?type=ugc");
    await expect(page.getByText("Product arrangement")).toBeVisible();
    await expect(page.getByRole("radio", { name: /Product provided by Business/ })).not.toBeChecked();
    await expect(page.getByRole("radio", { name: /Creator purchases product/ })).not.toBeChecked();
    await expect(page.getByRole("button", { name: "Save" })).toBeDisabled();
    await page.getByRole("radio", { name: /Creator purchases product/ }).check();
    await expect(page.getByText("Creator buys before creating content")).toBeVisible();
    await layout(page);
    if (width === 390) await screenshot(page, "390-business-ugc-arrangement");
    await open(page, "/business/campaigns");
    const businessCard = page.locator(".business-promotion-card").filter({ hasText: opportunity.title });
    await businessCard.getByRole("link", { name: "Manage campaign" }).click();
    await expect(page.getByText("Creator purchases", { exact: true })).toBeVisible();
    await layout(page);
    await open(page,"/business/campaigns");
    await page.getByRole("link",{name:"Edit details"}).click();
    await expect(page).toHaveURL(new RegExp(`/business/ugc/${opportunity.id}/edit$`));
    await page.getByRole("button",{name:"Edit",exact:true}).click();
    const editor=page.getByRole("dialog",{name:"Edit Promotion details"});
    await expect(editor).toBeVisible();
    await expect(editor.getByLabel("Promotion title",{exact:true})).toHaveValue(opportunity.title);
    await layout(page);

    for (const [alias, path] of [["creator", "/creator/discover"], ["customer", "/customer/offers"],
      ["cashier", "/checkout"], ["admin", "/admin"], ["operations-admin", "/admin/operations"]] as const) {
      await login(context, alias);
      await open(page, path);
      if (alias === "creator") {
        const card = page.locator(".creator-opportunity-card").filter({ hasText: opportunity.title });
        await expect(card.getByText("Creator purchases product", { exact: true })).toBeVisible();
        await expect(card.getByRole("button", { name: "Request to Join" })).toBeVisible();
        await open(page, `/creator/ugc/${opportunity.id}`);
        await expect(page.getByText("Creator purchases product", { exact: true })).toBeVisible();
        await expect(page.getByRole("button", { name: "Request to Join" })).toBeVisible();
      }
      const bell = page.getByRole("link", { name: "Your notifications" });
      if (["admin", "operations-admin"].includes(alias)) await expect(bell).toHaveCount(0);
      else await expect(bell.locator("svg")).toBeVisible();
      const gear = page.locator(".topbar-right svg:has(circle[cx='12'][cy='12'])");
      if (!["cashier", "operations-admin"].includes(alias)) await expect(gear).toBeVisible();
      expect(await page.evaluate(() => Number.parseFloat(getComputedStyle(document.documentElement).fontSize))).toBeGreaterThanOrEqual(18);
      await layout(page);
      if (width === 390 && ["creator", "admin"].includes(alias)) await screenshot(page, `390-${alias}-icon-readability`);
    }
  });
}

test("Business tracking tabs fit three-digit counts on a 320px screen", async ({page,context}) => {
  await page.setViewportSize({width:320,height:844});
  const rows=["Pending","Active","Completed"].flatMap(group=>Array.from({length:101},(_,index)=>({
    key:`${group}-${index}`,id:`${group}-${index}`,campaignId:"synthetic-campaign",kind:"UGC campaign",title:`${group} campaign ${index}`,
    group,status:group==="Active"?"Creator work completed · Customer offer active":group,budget:5000,remaining:group==="Completed"?0:5000,
    dueAtUtc:"2027-12-01T10:00:00Z",primaryAction:"Manage campaign",actionPath:"/business/ugc/synthetic-campaign",creator:null,remainingDays:group==="Active"?12:null,
  })));
  await page.route("**/api/business/promotion-management",route=>route.fulfill({json:rows}));
  await login(context,"business");await open(page,"/business/campaigns?tab=Active");
  const tabs=page.getByRole("tablist",{name:"Promotion management"});
  for(const group of ["Pending","Active","Completed"]){
    const tab=tabs.getByRole("tab",{name:`${group} (101)`});await expect(tab).toBeVisible();await tab.click();
    await expect(tab).toHaveAttribute("aria-selected","true");await layout(page);
  }
});
