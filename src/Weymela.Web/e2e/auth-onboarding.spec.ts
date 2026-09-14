import { test, expect } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";
import { mkdirSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import type { SessionUser } from "../src/api/types";

type RuntimeSignals = {
  consoleErrors: string[];
  pageErrors: string[];
  failedRequests: string[];
  apiResponses: { method: string; path: string; status: number }[];
};
type StepRecord = { name: string; startedAtUtc: string; elapsedMs?: number; status: "running" | "passed" | "failed"; error?: string };

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

async function runStep<T>(steps: StepRecord[], name: string, action: () => Promise<T>, timeoutMs = 8000) {
  const entry: StepRecord = { name, startedAtUtc: new Date().toISOString(), status: "running" };
  steps.push(entry);
  const operation = action();
  let timer: ReturnType<typeof setTimeout> | undefined;
  const deadline = new Promise<never>((_, reject) => {
    timer = setTimeout(() => reject(new Error(`${name} exceeded ${timeoutMs}ms`)), timeoutMs);
  });
  try {
    const result = await Promise.race([operation, deadline]);
    entry.status = "passed";
    entry.elapsedMs = Date.now() - Date.parse(entry.startedAtUtc);
    return result;
  } catch (error) {
    entry.status = "failed";
    entry.elapsedMs = Date.now() - Date.parse(entry.startedAtUtc);
    entry.error = redact(error instanceof Error ? error.message : String(error), 500);
    throw error;
  } finally {
    if (timer) clearTimeout(timer);
  }
}

function installRuntimeSignals(page: import("@playwright/test").Page): RuntimeSignals {
  const signals: RuntimeSignals = { consoleErrors: [], pageErrors: [], failedRequests: [], apiResponses: [] };
  page.on("console", message => { if (message.type() === "error") record(signals.consoleErrors, redact(message.text())); });
  page.on("pageerror", error => record(signals.pageErrors, redact(error.message)));
  page.on("requestfailed", request => record(signals.failedRequests, `${request.method()} ${new URL(request.url()).pathname}`));
  page.on("response", response => {
    const url = new URL(response.url());
    if (url.pathname.startsWith("/api/session") || url.pathname.startsWith("/api/onboarding") || url.pathname.startsWith("/api/device"))
      record(signals.apiResponses, { method: response.request().method(), path: url.pathname, status: response.status() });
  });
  return signals;
}

async function visibleCount(locator: import("@playwright/test").Locator) {
  try {
    return await locator.evaluateAll(elements => elements.filter(element => {
      const node = element as HTMLElement;
      return node.checkVisibility();
    }).length);
  } catch {
    return "unavailable";
  }
}

async function captureCustomerFormFailure(page: import("@playwright/test").Page, context: import("@playwright/test").BrowserContext, signals: RuntimeSignals, marker: unknown, steps: StepRecord[], error: unknown) {
  const unavailable = "unavailable";
  try {
    const pageClosed = (() => { try { return page.isClosed(); } catch { return unavailable; } })();
    const body = await page.locator("body").innerText({ timeout: 1000 }).catch(() => unavailable);
    const main = await page.locator("main").innerHTML({ timeout: 1000 }).catch(() => unavailable);
    const selectors = {
      useAsCustomer: page.getByRole("button", { name: /^Use as Customer/ }),
      displayName: page.getByLabel("Display name", { exact: true }),
      publicId: page.getByLabel("Public ID", { exact: true }),
      continue: page.getByRole("button", { name: "Continue", exact: true }),
    };
    const counts = await Promise.all(Object.entries(selectors).map(async ([name, locator]) => [name, { total: await locator.count().catch(() => unavailable), visible: await visibleCount(locator) }] as const));
    const evidence = {
      revision: marker,
      pageClosed,
      contextPages: (() => { try { return context.pages().length; } catch { return unavailable; } })(),
      url: (() => { try { return page.url(); } catch { return unavailable; } })(),
      title: await page.title().catch(() => unavailable),
      bodyText: redact(body, 8000),
      onboardingDom: redact(main, 12000),
      controls: Object.fromEntries(counts),
      signals,
      steps,
      failure: redact(error instanceof Error ? error.message : String(error)),
      capturedAtUtc: new Date().toISOString(),
    };
    try { mkdirSync(resolve(diagnosticRoot, "phase6-screenshots"), { recursive: true }); } catch { /* best effort */ }
    try { writeFileSync(diagnosticJson, `${JSON.stringify(evidence, null, 2)}\n`, { mode: 0o600 }); } catch { /* best effort */ }
    await page.screenshot({ path: diagnosticScreenshot, fullPage: true, animations: "disabled", timeout: 1500 }).catch(() => undefined);
  } catch {
    // Diagnostics must never replace the original Playwright failure.
  }
}

async function emailCode(context: Parameters<typeof login>[0], identifier: string, purpose: string, phone?: string, deliveryIdentifier = identifier) {
  const started = await context.request.post("/api/auth/email/start", {
    timeout: 10000,
    headers: { "X-Weymela-Request": "1" },
    data: { identifier, phone: phone ?? null, purpose },
  });
  expect(started.status()).toBe(202);
  const codeResponse = await context.request.get(`/__test/email-code?identifier=${encodeURIComponent(deliveryIdentifier.replace(/\s+/g, ""))}`, { timeout: 10000 });
  expect(codeResponse.status()).toBe(200);
  const { code } = await codeResponse.json() as { code: string };
  const verified = await context.request.post("/api/auth/email/verify", {
    timeout: 10000,
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

async function enrollDevice(context: Parameters<typeof login>[0], suffix: string) {
  const before = await context.request.get("/api/device/enrollment");
  expect(before.status()).toBe(200);
  await expect(before.json()).resolves.toMatchObject({ state: "EnrollmentRequired" });
  const enrolled = await context.request.post("/api/device/enrollment", {
    headers: { "X-Weymela-Request": "1", "Idempotency-Key": `browser-device-${suffix}` },
    data: { pin: "01234", confirmPin: "01234" },
  });
  expect(enrolled.status()).toBe(200);
  await expect(enrolled.json()).resolves.toMatchObject({ state: "Enrolled" });
  const after = await context.request.get("/api/device/enrollment");
  await expect(after.json()).resolves.toMatchObject({ state: "Enrolled" });
}

async function approveLatest(context: Parameters<typeof login>[0], role: "Customer" | "Creator" | "Business", publicId?: string) {
  await login(context, "admin");
  const roleValue = enrollmentRoles[role];
  const pending = await (await context.request.get("/api/admin/role-enrollments")).json() as { id: string; role: number; version: number; publicId?: string }[];
  const row = pending.find((item) => item.role === roleValue && (!publicId || item.publicId === publicId));
  expect(row, `pending ${role} request`).toBeTruthy();
  const reviewed = await context.request.post(`/api/admin/role-enrollments/${row!.id}/review`, {
    headers: {
      "X-Weymela-Request": "1",
      "Idempotency-Key": `browser-review-${role.toLowerCase()}-${row!.id}`,
    },
    data: { approve: true, expectedVersion: row!.version },
  });
  expect(reviewed.status()).toBe(200);
}

type AccountFixture = { suffix: string; email: string; phone: string; token: string; customerPublicId: string };
const enrollmentRoles = { Business: 1, Creator: 2, Customer: 3 } as const;
const enrollmentStatuses = { Pending: 0, Approved: 1, Rejected: 2 } as const;

async function createVerifiedAccount(context: Parameters<typeof login>[0], enroll = true): Promise<Omit<AccountFixture, "customerPublicId">> {
  const suffix = Date.now().toString() + Math.floor(Math.random() * 1_000_000).toString().padStart(6, "0");
  const email = `browser-${suffix}@example.com`;
  const phone = `+2519${suffix.slice(-8)}`;
  const token = await emailCode(context, email, "Signup", phone);
  await establishFirebaseSession(context, token, "Customer");
  if (enroll) await enrollDevice(context, suffix);
  return { suffix, email, phone, token };
}

async function activateCustomer(context: Parameters<typeof login>[0], account: Omit<AccountFixture, "customerPublicId">) {
  const customerPublicId = `CU-${account.suffix}`;
  const response = await context.request.post("/api/onboarding/profile", {
    headers: { "X-Weymela-Request": "1", "Idempotency-Key": `customer-${account.suffix}` },
    data: { role: "Customer", displayName: `Customer ${account.suffix}`, publicId: customerPublicId },
  });
  expect(response.status()).toBe(200);
  await expect((await context.request.get("/api/session", { timeout: 10000 })).json()).resolves.toMatchObject({ role: "Customer" });
  return { ...account, customerPublicId };
}

async function submitAdditionalProfile(context: Parameters<typeof login>[0], account: AccountFixture, role: "Creator" | "Business") {
  const publicId = `${role === "Creator" ? "CR" : "BUS"}-${account.suffix}`;
  const response = await context.request.post("/api/onboarding/profile", {
    headers: { "X-Weymela-Request": "1", "Idempotency-Key": `${role.toLowerCase()}-${account.suffix}` },
    data: { role, displayName: `${role} ${account.suffix}`, publicId, region: "Addis Ababa", category: "Food", submission: `${role} test profile` },
  });
  expect(response.status()).toBe(200);
  const status = await context.request.get("/api/onboarding/status");
  expect(status.status()).toBe(200);
  const body = await status.json() as { profiles: { role: number; status: number; publicId: string }[] };
  expect(body.profiles).toEqual(expect.arrayContaining([
    expect.objectContaining({ role: enrollmentRoles[role], status: enrollmentStatuses.Pending, publicId }),
  ]));
  return publicId;
}

async function creatorChoiceDiagnostics(page: import("@playwright/test").Page, context: Parameters<typeof login>[0], signals: RuntimeSignals, marker: unknown) {
  const safeJson = async (path: string) => {
    const response = await context.request.get(path, { timeout: 5000 });
    const body = await response.json().catch(() => null) as { role?: string; activeProfileKey?: string | null; profiles?: { role?: string; subjectId?: string; businessId?: string | null; status?: string; publicId?: string }[] } | null;
    return {
      status: response.status(),
      ...(path === "/api/session" ? {
        role: body?.role ?? null,
        activeProfileKey: body?.activeProfileKey ?? null,
        profiles: (body?.profiles ?? []).map((profile) => ({ role: profile.role ?? null, subjectId: profile.subjectId ?? null, businessId: profile.businessId ?? null })),
      } : {
        profiles: (body?.profiles ?? []).map((profile) => ({ role: profile.role ?? null, status: profile.status ?? null, publicId: profile.publicId ?? null })),
      }),
    };
  };
  const controls = {
    customer: page.getByRole("button", { name: /^Use as Customer/ }),
    creator: page.getByRole("button", { name: /^Become a Creator/ }),
    business: page.getByRole("button", { name: /^Add a Business/ }),
  };
  const evidence = {
    revision: marker,
    url: page.url(),
    heading: await page.locator("main h1").innerText().catch(() => "unavailable"),
    session: await safeJson("/api/session").catch(() => ({ status: "unavailable" })),
    onboarding: await safeJson("/api/onboarding/status").catch(() => ({ status: "unavailable" })),
    controls: {
      useAsCustomer: await controls.customer.count().catch(() => "unavailable"),
      becomeCreator: await controls.creator.count().catch(() => "unavailable"),
      addBusiness: await controls.business.count().catch(() => "unavailable"),
    },
    signals,
    capturedAtUtc: new Date().toISOString(),
  };
  try { mkdirSync(resolve(diagnosticRoot, "phase6-screenshots"), { recursive: true }); } catch { /* best effort */ }
  try { writeFileSync(resolve(diagnosticRoot, "phase6-creator-choice-diagnostics.json"), `${JSON.stringify(evidence, null, 2)}\n`, { mode: 0o600 }); } catch { /* best effort */ }
  await page.screenshot({ path: resolve(diagnosticRoot, "phase6-screenshots/creator-choice-failure.png"), fullPage: true, animations: "disabled", timeout: 1500 }).catch(() => undefined);
}

const switchSignals = new WeakMap<import("@playwright/test").Page, RuntimeSignals>();
async function chooseProfile(page: import("@playwright/test").Page, label: "Customer" | "Creator" | "Business") {
  const signals = switchSignals.get(page) ?? installRuntimeSignals(page);
  switchSignals.set(page, signals);
  const paths = [new URL(page.url()).pathname];
  const onNavigation = (frame: import("@playwright/test").Frame) => {
    if (frame === page.mainFrame()) record(paths, new URL(frame.url()).pathname);
  };
  page.on("framenavigated", onNavigation);
  let stage = "selector-visible";
  try {
    const select = page.getByLabel("Switch profile", { exact: true });
    await expect(select).toBeVisible({ timeout: 5000 });
    const value = await select.locator("option").filter({ hasText: label }).first().getAttribute("value", { timeout: 3000 });
    expect(value).toBeTruthy();
    const [role, subjectId, businessId] = value!.split(":");
    expect(role).toBe(label);
    stage = "switch-response";
    // Register before the change event; selectOption does not await the async
    // React handler, the protected cookie, or the coordinated route transition.
    const [response] = await Promise.all([
      page.waitForResponse(response => response.request().method() === "POST"
        && new URL(response.url()).pathname === "/api/session/switch-profile", { timeout: 7000 }),
      select.selectOption(value!, { timeout: 5000 }),
    ]);
    expect(response.request().postDataJSON()).toEqual({ role: label, subjectId, businessId: businessId === "-" ? null : businessId });
    expect(response.status()).toBe(200);
    const switched = await response.json() as SessionUser;
    expect(switched).toMatchObject({ role: label, activeProfileKey: value });
    stage = "session-refresh";
    await expect.poll(async () => {
      const sessionResponse = await page.context().request.get("/api/session", { timeout: 3000 });
      expect(sessionResponse.status()).toBe(200);
      const session = await sessionResponse.json() as SessionUser;
      return { role: session.role, activeProfileKey: session.activeProfileKey };
    }, { timeout: 7000 }).toEqual({ role: label, activeProfileKey: value });
    stage = "workspace-settled";
    const destination = { Customer: /\/customer\/offers(?:[/?#]|$)/, Creator: /\/creator(?:[/?#]|$)/, Business: /\/business(?:[/?#]|$)/ }[label];
    await expect(page).toHaveURL(destination, { timeout: 7000 });
    await expect(page.locator("main h1")).toBeVisible({ timeout: 5000 });
    await expect(select).toBeVisible({ timeout: 5000 });
    await expect(select).toHaveValue(value!, { timeout: 5000 });
    await expect(page).toHaveURL(destination);
    expect(paths, "A successful switch must never visit an unauthorized workspace").not.toContain("/unauthorized");
  } catch (error) {
    // Allowlisted server state only. Never record cookie/token/header values or
    // arbitrary response bodies; diagnostics must not replace the real failure.
    try {
      const response = await page.context().request.get("/api/session", { timeout: 1500 }).catch(() => null);
      const session = response?.ok() ? await response.json().catch(() => null) as SessionUser | null : null;
      const evidence = {
        stage, selectedRole: label, url: new URL(page.url()).pathname, paths,
        session: {
          status: response?.status() ?? "unavailable",
          role: session?.role ?? "unavailable", activeProfileKey: session?.activeProfileKey ?? null,
          profiles: session?.profiles?.map(profile => ({ role: profile.role, subjectId: profile.subjectId, businessId: profile.businessId })) ?? [],
        },
        signals, failure: redact(error instanceof Error ? error.message : String(error)),
      };
      // stderr is retained in the existing JSON reporter artifact, as well as
      // the attachment. No workflow expansion to sensitive artifact paths.
      console.error("Profile switch diagnostics:", JSON.stringify(evidence));
      await test.info().attach("profile-switch-diagnostics", { body: JSON.stringify(evidence, null, 2), contentType: "application/json" });
    } catch { /* best effort; preserve the original assertion */ }
    throw error;
  } finally {
    page.off("framenavigated", onNavigation);
  }
}

async function buildMarker(context: Parameters<typeof login>[0]) {
  const response = await context.request.get("/__test/build-info");
  expect(response.status()).toBe(200);
  const marker = await response.json() as { revision: string };
  expect(marker.revision).toBe(process.env.V3_TEST_BUILD_REVISION ?? process.env.GITHUB_SHA ?? "local");
  return marker;
}

async function submitProfileFromPage(page: import("@playwright/test").Page, role: "Creator" | "Business", suffix: string) {
  await page.getByLabel("Display name", { exact: true }).fill(`${role} ${suffix}`);
  await page.getByLabel("Public ID", { exact: true }).fill(`${role === "Creator" ? "CR" : "BUS"}-${suffix}`);
  await page.getByLabel("Region", { exact: true }).fill("Addis Ababa");
  await page.getByLabel("Category", { exact: true }).fill("Food");
  const pending = page.waitForResponse(response => {
    const url = new URL(response.url());
    return response.request().method() === "POST" && url.pathname === "/api/onboarding/profile";
  }, { timeout: 7000 });
  await page.getByRole("button", { name: "Submit for review", exact: true }).click({ timeout: 7000 });
  const response = await pending;
  expect(response.status()).toBe(200);
  await expect(page.getByText(/Under review/)).toBeVisible();
  return `${role === "Creator" ? "CR" : "BUS"}-${suffix}`;
}

test("account-signup-and-customer-activation", async ({ page, context }) => {
  page.setDefaultTimeout(8000);
  const account = await createVerifiedAccount(context, false);
  await open(page, "/onboarding");
  await expect(page).toHaveURL(/\/pin-setup/);
  await expect(page.getByRole("heading", { name: "Set up your Weymela PIN" })).toBeVisible();
  for (const width of [375, 390, 393, 430, 768, 1366]) {
    await page.setViewportSize({ width, height: width < 700 ? 844 : 900 });
    await layout(page);
  }
  await page.getByLabel("5-digit PIN", { exact: true }).fill("01234");
  await page.getByLabel("Confirm 5-digit PIN", { exact: true }).fill("01234");
  const deviceEnrollment = page.waitForResponse(response => response.request().method() === "POST"
    && new URL(response.url()).pathname === "/api/device/enrollment", { timeout: 7000 });
  await page.getByRole("button", { name: "Continue", exact: true }).click();
  expect((await deviceEnrollment).status()).toBe(200);
  await expect(page).toHaveURL(/\/onboarding/);
  await page.getByRole("button", { name: /^Use as Customer/ }).click();
  await page.getByLabel("Display name", { exact: true }).fill(`Customer ${account.suffix}`);
  await page.getByLabel("Public ID", { exact: true }).fill(`CU-${account.suffix}`);
  const activation = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname === "/api/onboarding/profile", { timeout: 7000 });
  await page.getByRole("button", { name: "Continue", exact: true }).click();
  expect((await activation).status()).toBe(200);
  await expect((await context.request.get("/api/session")).json()).resolves.toMatchObject({ role: "Customer" });
});

test("server idle lock requires the authorized-device PIN and propagates across tabs", async ({ page, context }) => {
  page.setDefaultTimeout(8000);
  const account = await createVerifiedAccount(context);
  await activateCustomer(context, account);
  const second = await context.newPage();
  await open(page, "/customer/offers");
  await open(second, "/customer/offers");

  const idled = await context.request.post("/__test/device-session/idle");
  expect(idled.status()).toBe(204);
  const direct = await context.request.get("/api/customer/offers");
  expect(direct.status()).toBe(423);
  await expect(direct.json()).resolves.toMatchObject({ code: "SessionLocked" });

  await page.reload();
  await expect(page.getByRole("heading", { name: "Weymela is locked" })).toBeVisible();
  await expect(second.getByRole("heading", { name: "Weymela is locked" })).toBeVisible();
  for (const width of [375, 390, 393, 430, 768, 1440]) {
    await page.setViewportSize({ width, height: width < 700 ? 844 : 900 });
    await layout(page);
  }

  const session = await context.request.get("/api/session");
  expect(session.status()).toBe(423);
  const switchAttempt = await context.request.post("/api/session/switch-profile", {
    headers: { "X-Weymela-Request": "1", "Idempotency-Key": `locked-switch-${account.suffix}` },
    data: { role: "Customer", subjectId: "00000000-0000-0000-0000-000000000001", businessId: null },
  });
  expect(switchAttempt.status()).toBe(423);

  await page.getByLabel("5-digit PIN", { exact: true }).fill("99999");
  const rejected = page.waitForResponse(response => response.request().method() === "POST"
    && new URL(response.url()).pathname === "/api/device/unlock");
  await page.getByRole("button", { name: "Unlock", exact: true }).click();
  expect((await rejected).status()).toBe(401);
  await expect(page.getByRole("alert")).toContainText("incorrect");

  await page.getByLabel("5-digit PIN", { exact: true }).fill("01234");
  const unlocked = page.waitForResponse(response => response.request().method() === "POST"
    && new URL(response.url()).pathname === "/api/device/unlock");
  await page.getByRole("button", { name: "Unlock", exact: true }).click();
  expect((await unlocked).status()).toBe(200);
  await expect(page).toHaveURL(/\/customer\/offers/);
  await expect(page.getByRole("heading", { name: /Offers/ })).toBeVisible();
  await expect(second.getByRole("heading", { name: /Offers/ })).toBeVisible();
  await second.close();
});

test("customer-to-creator-enrollment", async ({ page, context }) => {
  page.setDefaultTimeout(8000);
  const account = await createVerifiedAccount(context);
  await activateCustomer(context, account);
  const signals = installRuntimeSignals(page);
  const marker = await buildMarker(context);
  await open(page, "/onboarding");
  await expect(page.locator("main h1")).toHaveText(/Choose how you want to use Weymela/);
  await expect(page.getByRole("button", { name: /^Add a Business/ })).toHaveCount(1);
  await expect(page.getByRole("button", { name: /^Use as Customer/ })).toHaveCount(0);
  await creatorChoiceDiagnostics(page, context, signals, marker);
  const creatorChoice = page.getByRole("button", { name: /^Become a Creator/ });
  await expect(creatorChoice).toHaveCount(1);
  await creatorChoice.click();
  await submitProfileFromPage(page, "Creator", account.suffix);
  await expect((await context.request.get("/api/session")).json()).resolves.toMatchObject({ role: "Customer" });
  await open(page, "/customer/offers");
});

test("creator-admin-approval-and-switch", async ({ page, context }) => {
  page.setDefaultTimeout(8000);
  const account = await createVerifiedAccount(context);
  const active = await activateCustomer(context, account);
  const creatorPublicId = await submitAdditionalProfile(context, active, "Creator");
  await approveLatest(context, "Creator", creatorPublicId);
  await establishFirebaseSession(context, account.token, "Customer");
  await open(page, "/customer/offers");
  await chooseProfile(page, "Creator");
  await expect(page).toHaveURL(/\/creator/);
});

test("customer-to-business-enrollment", async ({ page, context }) => {
  page.setDefaultTimeout(8000);
  const account = await createVerifiedAccount(context);
  await activateCustomer(context, account);
  await open(page, "/onboarding");
  await expect(page.locator("main h1")).toHaveText(/Choose how you want to use Weymela/);
  await expect(page.getByRole("button", { name: /^Become a Creator/ })).toHaveCount(1);
  await expect(page.getByRole("button", { name: /^Use as Customer/ })).toHaveCount(0);
  const businessChoice = page.getByRole("button", { name: /^Add a Business/ });
  await expect(businessChoice).toHaveCount(1);
  await businessChoice.click();
  await submitProfileFromPage(page, "Business", account.suffix);
  await expect((await context.request.get("/api/session")).json()).resolves.toMatchObject({ role: "Customer" });
  await open(page, "/customer/offers");
});

test("business-admin-approval-and-switch", async ({ page, context }) => {
  page.setDefaultTimeout(8000);
  const account = await createVerifiedAccount(context);
  const active = await activateCustomer(context, account);
  const businessPublicId = await submitAdditionalProfile(context, active, "Business");
  await approveLatest(context, "Business", businessPublicId);
  await establishFirebaseSession(context, account.token, "Customer");
  await open(page, "/customer/offers");
  await chooseProfile(page, "Business");
  await expect(page).toHaveURL(/\/business/);
});

test("multi-role-switching", async ({ page, context }) => {
  page.setDefaultTimeout(8000);
  const account = await createVerifiedAccount(context);
  const active = await activateCustomer(context, account);
  const creatorPublicId = await submitAdditionalProfile(context, active, "Creator");
  const businessPublicId = await submitAdditionalProfile(context, active, "Business");
  await approveLatest(context, "Creator", creatorPublicId);
  await approveLatest(context, "Business", businessPublicId);
  await establishFirebaseSession(context, account.token, "Customer");
  await open(page, "/customer/offers");
  const select = page.getByLabel("Switch profile", { exact: true });
  await expect(select.locator("option")).toHaveCount(3);
  const beforeSwitch = await context.request.get("/api/session");
  expect(beforeSwitch.status()).toBe(200);
  const approved = await beforeSwitch.json() as SessionUser;
  expect(approved.role).toBe("Customer");
  expect(approved.profiles?.map(profile => profile.role).sort()).toEqual(["Business", "Creator", "Customer"]);
  const tampered = await context.request.post("/api/session/switch-profile", {
    headers: { "X-Weymela-Request": "1" },
    data: { role: "Business", subjectId: "00000000-0000-0000-0000-000000000000", businessId: null },
  });
  expect(tampered.status()).toBe(400);
  await expect(tampered.json()).resolves.toMatchObject({ code: "Validation" });
  const afterTampering = await context.request.get("/api/session");
  expect(afterTampering.status()).toBe(200);
  await expect(afterTampering.json()).resolves.toMatchObject({ role: "Customer", activeProfileKey: approved.activeProfileKey });
  await chooseProfile(page, "Creator");
  await expect(page).toHaveURL(/\/creator/);
  await chooseProfile(page, "Business");
  await expect(page).toHaveURL(/\/business/);
});

test("multi-role onboarding full-chain smoke", async ({ page, context }) => {
  page.setDefaultTimeout(8000);
  page.setDefaultNavigationTimeout(12000);
  const steps: StepRecord[] = [];
  const signals = installRuntimeSignals(page);
  const markerResponse = await context.request.get("/__test/build-info");
  expect(markerResponse.status()).toBe(200);
  const marker = await markerResponse.json() as { revision: string };
  expect(marker.revision).toBe(process.env.V3_TEST_BUILD_REVISION ?? process.env.GITHUB_SHA ?? "local");
  const suffix = Date.now().toString();
  const email = `browser-${suffix}@example.com`;
  const phone = `+2519${suffix.slice(-8)}`;
  const token = await runStep(steps, "signup-verify", () => emailCode(context, email, "Signup", phone), 15000);
  await runStep(steps, "firebase-session", () => establishFirebaseSession(context, token, "Customer"), 12000);
  await runStep(steps, "device-enrollment", () => enrollDevice(context, suffix), 15000);
  await runStep(steps, "onboarding-session", async () => expect((await context.request.get("/api/session", { timeout: 10000 })).json()).resolves.toMatchObject({ role: "Onboarding" }), 12000);

  try {
    await runStep(steps, "onboarding-open", () => open(page, "/onboarding"), 15000);
    await runStep(steps, "customer-choice", () => page.getByRole("button", { name: /^Use as Customer/ }).click({ timeout: 7000 }), 8000);
    await runStep(steps, "customer-form-visible", async () => {
      await expect(page.getByLabel("Display name", { exact: true })).toBeVisible({ timeout: 7000 });
      await expect(page.getByLabel("Public ID", { exact: true })).toBeVisible({ timeout: 7000 });
    }, 8000);
    await runStep(steps, "customer-form-fill", () => page.getByLabel("Display name", { exact: true }).fill(`Customer ${suffix}`, { timeout: 7000 }), 8000);
  } catch (error) {
    await captureCustomerFormFailure(page, context, signals, marker, steps, error);
    throw error;
  }
  await runStep(steps, "customer-form-fill-public-id", () => page.getByLabel("Public ID", { exact: true }).fill(`CU-${suffix}`, { timeout: 7000 }), 8000);
  await runStep(steps, "customer-submit", async () => {
    const activationResponse = page.waitForResponse(response => {
      const url = new URL(response.url());
      return response.request().method() === "POST" && url.pathname === "/api/onboarding/profile";
    }, { timeout: 7000 });
    await page.getByRole("button", { name: "Continue", exact: true }).click({ timeout: 7000 });
    const response = await activationResponse;
    expect(response.status()).toBe(200);
  }, 12000);
  await runStep(steps, "customer-session-refresh", async () => expect((await context.request.get("/api/session", { timeout: 10000 })).json()).resolves.toMatchObject({ role: "Customer" }), 12000);

  // The same verified account can resolve through its phone alias; only the
  // server-stored email receives the code, and the custom-token subject stays stable.
  const phoneToken = await runStep(steps, "phone-signin", () => emailCode(context, phone, "DeviceEnrollment", undefined, email), 15000);
  expect(phoneToken).toBe(token);
  await runStep(steps, "firebase-phone-session", () => establishFirebaseSession(context, phoneToken), 12000);
  await runStep(steps, "creator-onboarding-open", () => open(page, "/onboarding"), 15000);
  await creatorChoiceDiagnostics(page, context, signals, marker);
  await runStep(steps, "creator-choice", () => page.getByRole("button", { name: /^Become a Creator/ }).click({ timeout: 7000 }), 8000);
  await page.getByLabel("Display name", { exact: true }).fill(`Creator ${suffix}`);
  await page.getByLabel("Public ID", { exact: true }).fill(`CR-${suffix}`);
  await page.getByLabel("Region", { exact: true }).fill("Addis Ababa");
  await page.getByLabel("Category", { exact: true }).fill("Food");
  await runStep(steps, "creator-request", () => page.getByRole("button", { name: "Submit for review", exact: true }).click({ timeout: 7000 }), 8000);
  await expect(page.getByText(/Under review/)).toBeVisible();
  await runStep(steps, "creator-admin-review", () => approveLatest(context, "Creator", `CR-${suffix}`), 15000);
  await runStep(steps, "creator-session", () => establishFirebaseSession(context, token, "Customer"), 12000);
  await runStep(steps, "creator-profile-switch", () => open(page, "/customer/offers"), 15000);
  await chooseProfile(page, "Creator");
  await expect(page).toHaveURL(/\/creator/);

  await runStep(steps, "business-onboarding-open", () => open(page, "/onboarding"), 15000);
  await page.getByRole("button", { name: /^Add a Business/ }).click();
  await page.getByLabel("Display name", { exact: true }).fill(`Business ${suffix}`);
  await page.getByLabel("Public ID", { exact: true }).fill(`BUS-${suffix}`);
  await page.getByLabel("Region", { exact: true }).fill("Addis Ababa");
  await page.getByLabel("Category", { exact: true }).fill("Food");
  await runStep(steps, "business-request", () => page.getByRole("button", { name: "Submit for review", exact: true }).click({ timeout: 7000 }), 8000);
  await expect(page.getByText(/Under review/)).toBeVisible();
  await runStep(steps, "business-admin-review", () => approveLatest(context, "Business", `BUS-${suffix}`), 15000);
  await runStep(steps, "business-session", () => establishFirebaseSession(context, token, "Creator"), 12000);
  await runStep(steps, "business-profile-switch", () => open(page, "/creator"), 15000);
  await chooseProfile(page, "Business");
  await expect(page).toHaveURL(/\/business/);
  await layout(page);
  await screenshot(page, "auth-multi-role-approved-profiles");
});
