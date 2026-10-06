import { expect, test, type BrowserContext, type Page } from "@playwright/test";
import { login, open } from "./helpers";

const requestHeaders = { "X-Weymela-Request": "1" };

async function apiPost(context: BrowserContext, path: string, body: unknown, key: string) {
  return context.request.post(`/api${path}`, {
    headers: { ...requestHeaders, "Idempotency-Key": key },
    data: body,
  });
}

async function apiJson<T>(context: BrowserContext, path: string): Promise<T> {
  const response = await context.request.get(`/api${path}`, { headers: requestHeaders });
  expect(response.ok()).toBeTruthy();
  return (await response.json()) as T;
}

async function assertCustomerAbsent(
  context: BrowserContext,
  page: Page,
  title: string,
  allocationId?: string,
) {
  await login(context, "customer");
  const offers = await apiJson<Array<{ id: string; offer: string; slogan?: string }>>(
    context,
    "/customer/offers",
  );
  expect(offers).not.toContainEqual(expect.objectContaining({ id: allocationId }));
  expect(offers.some((offer) => offer.offer === title || offer.slogan === title)).toBeFalsy();
  await open(page, "/customer/offers");
  await expect(page.locator("main")).not.toContainText(title);
}

test("Customer stays absent from the regular workflow until Creator Go Live", async ({ page, context }) => {
  const title = `Views and Sales UI contract ${Date.now()}`;
  await login(context, "business");

  const createdResponse = await apiPost(context, "/business/campaigns", {
    title,
    description: "A deterministic visibility contract brief.",
    type: "View & Sale",
    campaignBudget: 500,
    requirements: "One original short video.",
    category: "Food",
    region: "Addis Ababa",
    minimumVerifiedFollowers: 0,
    startUtc: new Date(Date.now() - 60_000).toISOString(),
    endUtc: new Date(Date.now() + 5 * 86400_000).toISOString(),
    slogan: title,
  }, "ui-contract-create");
  expect(createdResponse.ok()).toBeTruthy();
  const promotionId = ((await createdResponse.json()) as { id: string }).id;

  await assertCustomerAbsent(context, page, title);
  await login(context, "business");

  const draft = await apiJson<{ campaign: { version: number } }>(context, `/business/campaigns/${promotionId}`);
  const earlyPost = await apiPost(context, `/business/campaigns/${promotionId}/publish`, {
    version: draft.campaign.version,
  }, "ui-contract-post-before-funding");
  expect(earlyPost.status()).toBe(400);
  await assertCustomerAbsent(context, page, title);
  await login(context, "business");

  const wallet = await apiJson<{ version: number }>(context, "/business/wallet");
  const funded = await apiPost(context, `/business/campaigns/${promotionId}/fund`, {
    campaignVersion: draft.campaign.version,
    walletVersion: wallet.version,
  }, "ui-contract-fund");
  expect(funded.ok()).toBeTruthy();
  await assertCustomerAbsent(context, page, title);
  await login(context, "business");

  const fundedDetails = await apiJson<{ campaign: { version: number } }>(context, `/business/campaigns/${promotionId}`);
  const posted = await apiPost(context, `/business/campaigns/${promotionId}/publish`, {
    version: fundedDetails.campaign.version,
  }, "ui-contract-post");
  expect(posted.ok()).toBeTruthy();
  await assertCustomerAbsent(context, page, title);

  await login(context, "other-creator");
  const discovery = await apiJson<Array<{ id: string }>>(context, "/creator/discover");
  expect(discovery.some((row) => row.id === promotionId)).toBeTruthy();
  const applicationResponse = await apiPost(context, `/creator/campaigns/${promotionId}/join`, {
    message: "I can create this story.",
    contentConcept: "A focused local story.",
  }, "ui-contract-apply");
  expect(applicationResponse.ok()).toBeTruthy();
  await assertCustomerAbsent(context, page, title);

  await login(context, "business");
  const application = (await apiJson<{ applicants: Array<{ id: string; status: string }> }>(
    context,
    `/business/campaigns/${promotionId}`,
  )).applicants.find((row) => row.status === "Pending");
  expect(application).toBeDefined();
  const approvalDetails = await apiJson<{ campaign: { version: number } }>(context, `/business/campaigns/${promotionId}`);
  const allocationResponse = await apiPost(context, `/business/applicants/${application!.id}/approve`, {
    amount: 200,
    version: approvalDetails.campaign.version,
  }, "ui-contract-approve");
  expect(allocationResponse.ok()).toBeTruthy();
  const allocationId = ((await allocationResponse.json()) as { id: string }).id;
  await assertCustomerAbsent(context, page, title, allocationId);

  await login(context, "other-creator");
  const submitted = await apiPost(context, `/creator/creator-budgets/${allocationId}/content`, {
    provider: "TikTok",
    externalContentId: `ui-contract-revision-one-${Date.now()}`,
  }, "ui-contract-submit-one");
  expect(submitted.ok()).toBeTruthy();
  await assertCustomerAbsent(context, page, title, allocationId);

  await login(context, "business");
  const firstSubmission = (await apiJson<Array<{ submissionId: string; promotion: string }>>(
    context,
    "/business/promotion-content-submissions",
  )).find((row) => row.promotion === title);
  expect(firstSubmission).toBeDefined();
  const changes = await apiPost(context, `/business/promotion-content-submissions/${firstSubmission!.submissionId}/review`, {
    action: "requestchanges",
    feedback: "Please revise the opening.",
  }, "ui-contract-request-changes");
  expect(changes.ok()).toBeTruthy();
  await assertCustomerAbsent(context, page, title, allocationId);

  await login(context, "other-creator");
  const revised = await apiPost(context, `/creator/creator-budgets/${allocationId}/content`, {
    provider: "TikTok",
    externalContentId: `ui-contract-revision-two-${Date.now()}`,
  }, "ui-contract-submit-two");
  expect(revised.ok()).toBeTruthy();
  await assertCustomerAbsent(context, page, title, allocationId);

  await login(context, "business");
  const latestSubmission = (await apiJson<Array<{ submissionId: string; promotion: string; revisionNumber: number }>>(
    context,
    "/business/promotion-content-submissions",
  )).filter((row) => row.promotion === title).sort((a, b) => b.revisionNumber - a.revisionNumber)[0];
  expect(latestSubmission).toBeDefined();
  const approved = await apiPost(context, `/business/promotion-content-submissions/${latestSubmission!.submissionId}/review`, {
    action: "approve",
    feedback: null,
  }, "ui-contract-approve-latest");
  expect(approved.ok()).toBeTruthy();
  await assertCustomerAbsent(context, page, title, allocationId);

  await login(context, "other-creator");
  const live = await apiPost(context, `/creator/creator-budgets/${allocationId}/go-live`, {}, "ui-contract-go-live");
  expect(live.ok()).toBeTruthy();
  await login(context, "customer");
  const offers = await apiJson<Array<{ id: string; slogan?: string }>>(context, "/customer/offers");
  expect(offers).toContainEqual(expect.objectContaining({ id: allocationId, slogan: title }));
  await open(page, "/customer/offers");
  await expect(page.locator("main")).toContainText(title);
});
