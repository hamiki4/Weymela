import { test, expect } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";
import { mkdirSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";

type RuntimeSignals = {
  consoleErrors: string[];
  pageErrors: string[];
  failedRequests: string[];
  apiResponses: { method: string; path: string; status: number }[];
};

const diagnosticRoot = resolve("../../.artifacts");
const diagnosticScreenshot = resolve(diagnosticRoot, "phase6-screenshots/customer-form-failure.png");
const diagnosticJson = resolve(diagnosticRoot, "phase6-customer-form-diagnostics.json");

function redact(value: string, limit = 2000) {
  return value
    .replace(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/gi, "[REDACTED_EMAIL]")
    .replace(/\+?\d[\d\s().-]{7,}\d/g, "[REDACTED_PHONE]")
    .replace(/(authorization|cookie|token|password|pin|secret|code)\s*[:=]\s*[^\s,;]+/gi, "$1=[REDACTED]")
    .slice(0, limit);
}

function record<T>(items: T[], value: T, limit = 50) {
  if (items.length < limit) items.push(value);
}

function installRuntimeSignals(page: import("@playwright/test").Page): RuntimeSignals {
  const signals: RuntimeSignals = { consoleErrors: [], pageErrors: [], failedRequests: [], apiResponses: [] };
  page.on("console", message => { if (message.type() === "error") record(signals.consoleErrors, redact(message.text())); });
  page.on("pageerror", error => record(signals.pageErrors, redact(error.message)));
  page.on("requestfailed", request => record(signals.failedRequests, `${request.method()} ${new URL(request.url()).pathname}`));
  page.on("response", response => {
    const url = new URL(response.url());
    if (url.pathname.startsWith("/api/session") || url.pathname.startsWith("/api/onboarding"))
      record(signals.apiResponses, { method: response.request().method(), path: url.pathname, status: response.status() });
  });
  return signals;
}

async function visibleCount(locator: import("@playwright/test").Locator) {
  return locator.evaluateAll(elements => elements.filter(element => {
    const node = element as HTMLElement;
    return node.checkVisibility();
  }).length);
}

async function captureCustomerFormFailure(page: import("@playwright/test").Page, signals: RuntimeSignals, marker: unknown, error: unknown) {
  mkdirSync(resolve(diagnosticRoot, "phase6-screenshots"), { recursive: true });
  const body = await page.locator("body").innerText().catch(() => "<body unavailable>");
  const main = await page.locator("main").innerHTML().catch(() => "<main unavailable>");
  const selectors = {
    useAsCustomer: page.getByRole("button", { name: "Use as Customer", exact: true }),
    displayName: page.getByLabel("Display name", { exact: true }),
    publicId: page.getByLabel("Public ID", { exact: true }),
    continue: page.getByRole("button", { name: "Continue", exact: true }),
  };
  const counts = await Promise.all(Object.entries(selectors).map(async ([name, locator]) => [name, { total: await locator.count(), visible: await visibleCount(locator) }] as const));
  const evidence = {
    revision: marker,
    url: page.url(),
    title: await page.title().catch(() => "<title unavailable>"),
    bodyText: redact(body, 8000),
    onboardingDom: redact(main, 12000),
    controls: Object.fromEntries(counts),
    signals,
    failure: redact(error instanceof Error ? error.message : String(error)),
    capturedAtUtc: new Date().toISOString(),
  };
  writeFileSync(diagnosticJson, `${JSON.stringify(evidence, null, 2)}\n`, { mode: 0o600 });
  await page.screenshot({ path: diagnosticScreenshot, fullPage: true, animations: "disabled" }).catch(() => undefined);
}

async function emailCode(context: Parameters<typeof login>[0], identifier: string, purpose: string, phone?: string, deliveryIdentifier = identifier) {
  const started = await context.request.post("/api/auth/email/start", {
    headers: { "X-Weymela-Request": "1" },
    data: { identifier, phone: phone ?? null, purpose },
  });
  expect(started.status()).toBe(202);
  const codeResponse = await context.request.get(`/__test/email-code?identifier=${encodeURIComponent(deliveryIdentifier.replace(/\s+/g, ""))}`);
  expect(codeResponse.status()).toBe(200);
  const { code } = await codeResponse.json() as { code: string };
  const verified = await context.request.post("/api/auth/email/verify", {
    headers: { "X-Weymela-Request": "1" },
    data: { identifier, purpose, code },
  });
  expect(verified.status()).toBe(200);
  return (await verified.json() as { customToken: string }).customToken;
}

async function establishFirebaseSession(context: Parameters<typeof login>[0], token: string, preferredRole?: "Customer" | "Creator" | "Business") {
  const response = await context.request.post("/api/auth/firebase/session", {
    headers: { "X-Weymela-Request": "1" },
    data: { idToken: token },
  });
  if (response.status() === 204) return;
  expect(response.status()).toBe(409);
  const body = await response.json() as { profiles: { role: string; subjectId: string; businessId: string | null }[] };
  const profile = body.profiles.find((item) => item.role === preferredRole) ?? body.profiles[0];
  expect(profile).toBeTruthy();
  const selected = await context.request.post("/api/auth/firebase/session", {
    headers: { "X-Weymela-Request": "1" },
    data: { idToken: token, profileRole: profile!.role, profileSubjectId: profile!.subjectId, profileBusinessId: profile!.businessId },
  });
  expect(selected.status()).toBe(204);
}

async function approveLatest(context: Parameters<typeof login>[0], role: "Customer" | "Creator" | "Business") {
  await login(context, "admin");
  const pending = await (await context.request.get("/api/admin/role-enrollments")).json() as { id: string; role: string; version: number }[];
  const row = pending.find((item) => item.role === role);
  expect(row, `pending ${role} request`).toBeTruthy();
  const reviewed = await context.request.post(`/api/admin/role-enrollments/${row!.id}/review`, {
    headers: { "X-Weymela-Request": "1" },
    data: { approve: true, expectedVersion: row!.version },
  });
  expect(reviewed.status()).toBe(200);
}

test("one email-verified account can sign in by phone and complete independent profile approvals", async ({ page, context }) => {
  const signals = installRuntimeSignals(page);
  const markerResponse = await context.request.get("/__test/build-info");
  expect(markerResponse.status()).toBe(200);
  const marker = await markerResponse.json() as { revision: string };
  expect(marker.revision).toBe(process.env.V3_TEST_BUILD_REVISION ?? process.env.GITHUB_SHA ?? "local");
  const suffix = Date.now().toString();
  const email = `browser-${suffix}@example.com`;
  const phone = `+2519${suffix.slice(-8)}`;
  const token = await emailCode(context, email, "Signup", phone);
  await establishFirebaseSession(context, token, "Customer");
  await expect((await context.request.get("/api/session")).json()).resolves.toMatchObject({ role: "Onboarding" });

  await open(page, "/onboarding");
  try {
    await page.getByRole("button", { name: "Use as Customer", exact: true }).click();
    await page.getByLabel("Display name", { exact: true }).fill(`Customer ${suffix}`);
  } catch (error) {
    await captureCustomerFormFailure(page, signals, marker, error);
    throw error;
  }
  await page.getByLabel("Public ID", { exact: true }).fill(`CU-${suffix}`);
  await page.getByRole("button", { name: "Continue", exact: true }).click();
  await expect((await context.request.get("/api/session")).json()).resolves.toMatchObject({ role: "Customer" });

  // The same verified account can resolve through its phone alias; only the
  // server-stored email receives the code, and the custom-token subject stays stable.
  const phoneToken = await emailCode(context, phone, "DeviceEnrollment", undefined, email);
  expect(phoneToken).toBe(token);
  await establishFirebaseSession(context, phoneToken);
  await open(page, "/onboarding");
  await page.getByRole("button", { name: "Become a Creator", exact: true }).click();
  await page.getByLabel("Display name", { exact: true }).fill(`Creator ${suffix}`);
  await page.getByLabel("Public ID", { exact: true }).fill(`CR-${suffix}`);
  await page.getByLabel("Region", { exact: true }).fill("Addis Ababa");
  await page.getByLabel("Category", { exact: true }).fill("Food");
  await page.getByRole("button", { name: "Submit for review", exact: true }).click();
  await expect(page.getByText("Under review", { exact: true })).toBeVisible();
  await approveLatest(context, "Creator");
  await establishFirebaseSession(context, token, "Creator");
  await open(page, "/customer/offers");
  await expect(page.getByLabel("Switch profile", { exact: true })).toBeVisible();
  const creatorProfile = await page.getByLabel("Switch profile", { exact: true }).locator("option").filter({ hasText: "Creator" }).first().getAttribute("value");
  await page.getByLabel("Switch profile", { exact: true }).selectOption(creatorProfile!);
  await expect(page).toHaveURL(/\/creator/);

  await open(page, "/onboarding");
  await page.getByRole("button", { name: "Add a Business", exact: true }).click();
  await page.getByLabel("Display name", { exact: true }).fill(`Business ${suffix}`);
  await page.getByLabel("Public ID", { exact: true }).fill(`BUS-${suffix}`);
  await page.getByLabel("Region", { exact: true }).fill("Addis Ababa");
  await page.getByLabel("Category", { exact: true }).fill("Food");
  await page.getByRole("button", { name: "Submit for review", exact: true }).click();
  await expect(page.getByText("Under review", { exact: true })).toBeVisible();
  await approveLatest(context, "Business");
  await establishFirebaseSession(context, token, "Creator");
  await open(page, "/creator");
  await expect(page.getByLabel("Switch profile", { exact: true })).toBeVisible();
  const businessProfile = await page.getByLabel("Switch profile", { exact: true }).locator("option").filter({ hasText: "Business" }).first().getAttribute("value");
  await page.getByLabel("Switch profile", { exact: true }).selectOption(businessProfile!);
  await expect(page).toHaveURL(/\/business/);
  await layout(page);
  await screenshot(page, "auth-multi-role-approved-profiles");
});
