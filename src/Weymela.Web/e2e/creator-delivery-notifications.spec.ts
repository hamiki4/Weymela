import { test, expect } from "@playwright/test";
import { createPublishedUgc, ensureBusinessFunds, layout, login, open, reviewMp4 } from "./helpers";

test("direct UGC Only delivery earns on approval and exposes an authorized download on mobile", async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(context, "business"); await ensureBusinessFunds(context, 500);
  await open(page, "/business/campaigns/new?type=ugc");
  const title = `Direct delivery ${Date.now()}`;
  await page.getByLabel("Promotion title", { exact: true }).fill(title);
  await page.getByLabel("Deliverable", { exact: true }).selectOption("DirectVideoDelivery");
  await expect(page.getByLabel("Content type", { exact: true })).toBeDisabled();
  await expect(page.getByRole("group", { name: "Publication platforms" })).toHaveCount(0);
  await page.getByLabel("Application closes", { exact: true }).fill(new Date(Date.now() + 86400000).toISOString().slice(0, 16));
  await page.getByLabel("Content due", { exact: true }).fill(new Date(Date.now() + 172800000).toISOString().slice(0, 16));
  await page.getByLabel("Creator payment (ETB)", { exact: true }).fill("500");
  await page.getByLabel("Instructions", { exact: true }).fill("Deliver the finished fixture video privately.");
  await page.getByRole("radio", { name: /Product provided by Business/ }).check();
  await page.getByRole("button", { name: "Publish", exact: true }).click();
  await expect(page).toHaveURL(/\/business\/ugc\/[0-9a-f-]{36}$/);
  const id = page.url().split("/").at(-1)!;
  const headers = { "X-Weymela-Request": "1", "Idempotency-Key": crypto.randomUUID() };
  await login(context, "creator");
  const joined = await context.request.post(`/api/creator/ugc/${id}/request`, { headers }); expect(joined.ok()).toBeTruthy();
  const request = await joined.json() as { id: string };
  await login(context, "business");
  const approved = await context.request.post(`/api/business/ugc/requests/${request.id}/approve`, { headers: { ...headers, "Idempotency-Key": crypto.randomUUID() }, data: {} });
  expect(approved.ok()).toBeTruthy(); const assignment = await approved.json() as { id: string };
  await login(context, "creator"); await open(page, "/creator/promotions");
  const work = page.locator("article").filter({ has: page.getByRole("heading", { name: title, exact: true }) });
  await work.locator("summary").click();
  await work.getByLabel("Final video", { exact: true }).setInputFiles({ name: "finished.mp4", mimeType: "video/mp4", buffer: reviewMp4 });
  await work.getByRole("button", { name: "Submit for Review", exact: true }).click();
  await expect(work.getByText("Waiting for Business review", { exact: true })).toBeVisible();
  await expect(work.getByRole("button", { name: "Go Live", exact: true })).toHaveCount(0);
  await login(context, "business"); await open(page, `/business/ugc/${id}`);
  const review = page.locator(".business-collaboration-card").filter({ hasText: "Bella" });
  await review.getByRole("button", { name: "Approve", exact: true }).click();
  const download = review.getByRole("link", { name: "Download approved video" }); await expect(download).toBeVisible();
  const response = await context.request.get((await download.getAttribute("href"))!); expect(response.ok()).toBeTruthy();
  expect(response.headers()["content-disposition"]).toContain("attachment");
  expect(await response.body()).toEqual(reviewMp4);
  const rows = await (await context.request.get(`/api/business/ugc/${id}`)).json() as { assignments: { id: string; completedAtUtc: string | null; publication: unknown }[] };
  expect(rows.assignments.find(row => row.id === assignment.id)?.completedAtUtc).toBeTruthy();
  expect(rows.assignments.find(row => row.id === assignment.id)?.publication).toBeNull();
  await layout(page);
});

test("unread notification rows are bold and read state stays normal after reload", async ({ page, context }) => {
  const id = await createPublishedUgc(context, `Notification evidence ${Date.now()}`, "other-creator");
  await login(context, "business");
  await expect.poll(async () => {
    const inbox = await (await context.request.get("/api/notifications")).json() as {
      items: { route: string; title: string; readAtUtc: string | null }[];
    };
    return inbox.items.some(row => row.route === `/business/ugc/${id}` && row.title === "New UGC request" && !row.readAtUtc);
  }, { timeout: 15000 }).toBe(true);
  await open(page, "/notifications");
  const notification = page.locator(".notification-item")
    .filter({ has: page.locator(`a[data-notification-open][href="/business/ugc/${id}"]`) })
    .filter({ hasText: "New UGC request" });
  await expect(notification).toHaveAttribute("data-unread", "true");
  await expect(notification.locator(".notification-row > span:first-child")).toHaveCSS("font-weight", "700");
  await expect(notification.locator("p")).toHaveCount(0);
  await notification.getByRole("link", { name: "New UGC request", exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/business/ugc/${id}$`));
  await open(page, "/notifications");
  await expect(notification).toHaveAttribute("data-unread", "false");
  await page.reload();
  await expect(notification.locator(".notification-row > span:first-child")).toHaveCSS("font-weight", "400");
  await expect(notification.locator("p")).toHaveCount(0);
});
