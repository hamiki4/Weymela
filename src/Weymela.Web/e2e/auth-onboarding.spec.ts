import { test, expect } from "@playwright/test";
import { layout, login, open, screenshot } from "./helpers";
import { mkdirSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { actorRoleWireValues } from "../src/api/actorRoleContract";
import type { SessionUser } from "../src/api/types";

async function fillPin(page: import("@playwright/test").Page, label: string, pin: string) {
  if (!/^[0-9]{5}$/.test(pin)) throw new Error("Test PIN must be five digits.");
  const cells = page.getByRole("group", { name: label, exact: true }).locator("input");
  await expect(cells).toHaveCount(5);
  for (let index = 0; index < 5; index++) await cells.nth(index).fill(pin[index]);
}

async function pinTapTargets(page: import("@playwright/test").Page) {
  const targets = await page.locator(".pin-input-cells input").evaluateAll(elements => elements.map(element => {
    const rect = element.getBoundingClientRect();
    return rect.width >= 44 && rect.height >= 44;
  }));
  expect(targets.length).toBeGreaterThan(0);
  expect(targets.every(Boolean)).toBe(true);
}

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
      preferredName: page.getByLabel("Preferred name", { exact: true }),
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
  const roleValue = actorRoleWireValues[role];
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

type AccountFixture = { suffix: string; email: string; phone: string; password: string; token: string; customerPublicId: string };
const enrollmentStatuses = { Pending: 0, Approved: 1, Rejected: 2 } as const;

async function createVerifiedAccount(context: Parameters<typeof login>[0], enroll = true): Promise<Omit<AccountFixture, "customerPublicId">> {
  const suffix = Date.now().toString() + Math.floor(Math.random() * 1_000_000).toString().padStart(6, "0");
  const email = `browser-${suffix}@example.com`;
  const phone = `+2519${suffix.slice(-8)}`;
  const password = `Browser passphrase ${suffix}`;
  const token = await emailCode(context, email, "Signup");
  await establishFirebaseSession(context, token, "Customer");
  const security = await context.request.post("/api/account/password-credential", {
    headers: { "X-Weymela-Request": "1" }, data: { phone, password, confirmPassword: password },
  });
  expect(security.status()).toBe(204);
  if (enroll) {
    await enrollDevice(context, suffix);
  }
  return { suffix, email, phone, password, token };
}

async function activateCustomer(context: Parameters<typeof login>[0], account: Omit<AccountFixture, "customerPublicId">) {
  const legalResponse = await context.request.get("/api/onboarding/legal");
  expect(legalResponse.status()).toBe(200);
  const legal = await legalResponse.json() as { documents: { kind: string; documentId: string; contentHash: string }[] };
  const terms = legal.documents.find(document => document.kind === "TermsOfService")!;
  const privacy = legal.documents.find(document => document.kind === "PrivacyPolicy")!;
  const response = await context.request.post("/api/onboarding/profile", {
    headers: { "X-Weymela-Request": "1", "Idempotency-Key": `customer-${account.suffix}` },
    data: { role: "Customer", displayName: `Customer ${account.suffix}`,
      accountLegal: {
        termsOfService: { documentId: terms.documentId, contentHash: terms.contentHash, accepted: true },
        privacyPolicy: { documentId: privacy.documentId, contentHash: privacy.contentHash, accepted: true }
      } },
  });
  expect(response.status()).toBe(200);
  const customerPublicId = (await response.json() as { publicId: string }).publicId;
  expect(customerPublicId).toMatch(/^CU-[A-F0-9]{32}$/);
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
    expect.objectContaining({ role: actorRoleWireValues[role], status: enrollmentStatuses.Pending, publicId }),
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
    const select = page.locator("aside.sidebar").getByLabel("Switch profile", { exact: true });
    await expect(select).toBeVisible({ timeout: 5000 });
    const value = await select.locator("option").filter({ hasText: label }).first().getAttribute("value", { timeout: 3000 });
    expect(value).toBeTruthy();
    const before = await page.context().request.get("/api/session", { timeout: 3000 });
    expect(before.status()).toBe(200);
    const available = await before.json() as SessionUser;
    const selected = available.profiles?.[Number(value)];
    expect(selected?.role).toBe(label);
    const activeKey = `${selected!.role}:${selected!.subjectId}:${selected!.businessId ?? "-"}`;
    stage = "switch-response";
    // Register before the change event; selectOption does not await the async
    // React handler, the protected cookie, or the coordinated route transition.
    const [response] = await Promise.all([
      page.waitForResponse(response => response.request().method() === "POST"
        && new URL(response.url()).pathname === "/api/session/switch-profile", { timeout: 7000 }),
      select.selectOption(value!, { timeout: 5000 }),
    ]);
    expect(response.request().postDataJSON()).toEqual({ role: label, subjectId: selected!.subjectId, businessId: selected!.businessId });
    expect(response.status()).toBe(200);
    const switched = await response.json() as SessionUser;
    expect(switched).toMatchObject({ role: label, activeProfileKey: activeKey });
    stage = "session-refresh";
    await expect.poll(async () => {
      const sessionResponse = await page.context().request.get("/api/session", { timeout: 3000 });
      expect(sessionResponse.status()).toBe(200);
      const session = await sessionResponse.json() as SessionUser;
      return { role: session.role, activeProfileKey: session.activeProfileKey };
    }, { timeout: 7000 }).toEqual({ role: label, activeProfileKey: activeKey });
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
          role: session?.role ?? "unavailable", activeRole: session?.activeProfileKey?.split(":", 1)[0] ?? null,
          profileRoles: session?.profiles?.map(profile => profile.role) ?? [],
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

test("account-signup-and-customer-activation", async ({ page, context }) => {
  page.setDefaultTimeout(8000);
  const account = await createVerifiedAccount(context, false);
  await open(page, "/onboarding");
  await expect(page).toHaveURL(/\/pin-setup/);
  await expect(page.getByRole("heading", { name: "Create your PIN" })).toBeVisible();
  for (const width of [375, 390, 393, 430, 768, 1366]) {
    await page.setViewportSize({ width, height: width === 375 ? 667 : width < 700 ? 844 : 900 });
    await layout(page);
    await pinTapTargets(page);
  }
  await page.getByRole("group", { name: "Create PIN", exact: true }).locator("input").first().click();
  await page.keyboard.type("01234");
  await expect(page.getByRole("group", { name: "Create PIN", exact: true }).locator("input").last()).toHaveValue("4");
  await fillPin(page, "Confirm PIN", "01234");
  const deviceEnrollment = page.waitForResponse(response => response.request().method() === "POST"
    && new URL(response.url()).pathname === "/api/device/enrollment", { timeout: 7000 });
  await page.getByRole("button", { name: "Continue", exact: true }).click();
  expect((await deviceEnrollment).status()).toBe(200);
  await expect(page).toHaveURL(/\/onboarding/);
  await page.getByRole("button", { name: /^Use as Customer/ }).click();
  const consent = page.getByRole("checkbox", { name: /I agree to the Terms of Service and acknowledge the Privacy Policy/ });
  await expect(consent).not.toBeChecked();
  for (const [name, heading, marker] of [
    ["Terms of Service", "Terms of Service", "Weymela Pilot Terms of Service"],
    ["Privacy Policy", "Privacy Policy", "Weymela Pilot Privacy Policy"],
  ] as const) {
    const link = page.getByRole("link", { name });
    await expect(link).toBeVisible();
    const [documentPage] = await Promise.all([page.waitForEvent("popup"), link.click()]);
    await expect(documentPage.getByRole("heading", { name: heading })).toBeVisible();
    await expect(documentPage.getByText(marker, { exact: false })).toBeVisible();
    await expect(documentPage.getByText(/Pilot draft — Version pilot-draft-2026-09-16\.1/)).toBeVisible();
    await documentPage.close();
    await expect(consent).not.toBeChecked();
  }
  for (const width of [375, 390, 393, 430, 768, 1366]) {
    await page.setViewportSize({ width, height: width === 375 ? 667 : width < 700 ? 844 : 900 });
    await layout(page);
  }
  await page.getByLabel("Preferred name", { exact: true }).fill(`Customer ${account.suffix}`);
  await expect(page.getByLabel("Public ID", { exact: true })).toHaveCount(0);
  await consent.check();
  const activation = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname === "/api/onboarding/profile", { timeout: 7000 });
  await page.getByRole("button", { name: "Continue", exact: true }).click();
  expect((await activation).status()).toBe(200);
  await expect((await context.request.get("/api/session")).json()).resolves.toMatchObject({ role: "Customer" });
});

test("incomplete registration resumes the same identity at PIN setup without a request loop", async ({ browser }) => {
  const suffix = Date.now().toString() + Math.floor(Math.random() * 1_000_000).toString().padStart(6, "0");
  const email = `resume-${suffix}@example.com`;
  const phone = `+2519${suffix.slice(-8)}`;
  const password = `Resume account passphrase ${suffix}`;
  const firstContext = await browser.newContext();
  let originalToken = "";
  try {
    originalToken = await emailCode(firstContext, email, "Signup");
    await establishFirebaseSession(firstContext, originalToken);
    const initialSecurity = await firstContext.request.get("/api/account/security");
    expect(initialSecurity.status()).toBe(200);
    await expect(initialSecurity.json()).resolves.toMatchObject({ passwordEnrolled: false });
    const initialEnrollment = await firstContext.request.get("/api/device/enrollment");
    expect(initialEnrollment.status()).toBe(200);
    await expect(initialEnrollment.json()).resolves.toMatchObject({ state: "EnrollmentRequired" });
    const page = await firstContext.newPage();
    await page.goto("/security-setup");
    await expect(page.getByRole("heading", { name: "Secure your account" })).toBeVisible();
    await page.getByLabel("Phone number").fill(phone);
    await page.getByLabel("Password", { exact: true }).fill(password);
    await page.getByLabel("Confirm password", { exact: true }).fill(password);
    const secured = page.waitForResponse(response => response.request().method() === "POST"
      && new URL(response.url()).pathname === "/api/account/password-credential");
    await page.getByRole("button", { name: "Continue" }).click();
    expect((await secured).status()).toBe(204);
    await expect(page).toHaveURL(/\/pin-setup/);
    await expect(page.getByRole("heading", { name: "Create your PIN" })).toBeVisible();
  } finally {
    await firstContext.close();
  }

  const resumedContext = await browser.newContext();
  try {
    const resumedToken = await emailCode(resumedContext, email, "Signup");
    expect(resumedToken).toBe(originalToken);
    await establishFirebaseSession(resumedContext, resumedToken);
    const page = await resumedContext.newPage();
    const bootstrap: { path: string; status: number }[] = [];
    page.on("response", response => {
      const path = new URL(response.url()).pathname;
      if (["/api/device/access", "/api/session", "/api/account/security", "/api/device/enrollment"].includes(path))
        bootstrap.push({ path, status: response.status() });
    });
    await page.goto("/pin-setup");
    await expect(page.getByRole("heading", { name: "Create your PIN" })).toBeVisible();
    await page.waitForTimeout(250);
    expect(bootstrap.some(item => item.status === 429)).toBe(false);
    for (const path of ["/api/device/access", "/api/session", "/api/account/security", "/api/device/enrollment"])
      expect(bootstrap.filter(item => item.path === path)).toHaveLength(1);

    await fillPin(page, "Create PIN", "01234");
    await fillPin(page, "Confirm PIN", "01234");
    await page.getByRole("button", { name: "Continue" }).click();
    await expect(page).toHaveURL(/\/onboarding/);
    await expect(page.getByRole("heading", { name: "How do you want to use Weymela?" })).toBeVisible();
    await expect(page.getByRole("button", { name: /^Use as Customer/ })).toBeVisible();
    await expect(page.getByRole("button", { name: /^Become a Creator/ })).toBeVisible();
    await expect(page.getByRole("button", { name: /^Add a Business/ })).toBeVisible();
    await expect(page.getByText(/Platform Admin/)).toHaveCount(0);
  } finally {
    await resumedContext.close();
  }
});

test("password recovery uses a server-bound browser transaction", async ({ context, browser }) => {
  const account = await createVerifiedAccount(context);
  const recoveryContext = await browser.newContext();
  const recoveryPage = await recoveryContext.newPage();
  try {
    recoveryPage.setDefaultTimeout(8000);
    // BrowserHost keeps its seeded Admin fixture available for the broader E2E
    // suite. This page exercises the real public auth UI against the same real
    // API endpoints by hiding only that test-only persona selector response.
    await recoveryPage.route("**/api/auth/mode", route => route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ development: false, personas: null }),
    }));
    await recoveryPage.goto("/sign-in?intent=sign-in");
    await recoveryPage.getByRole("button", { name: "Forgot password?" }).click();
    await recoveryPage.getByLabel("Email address").fill(account.email);
    const recoveryStarted = recoveryPage.waitForResponse(
      response => response.request().method() === "POST"
        && new URL(response.url()).pathname === "/api/auth/email/start",
      { timeout: 20_000 },
    );
    await recoveryPage.getByRole("button", { name: "Continue" }).click();
    expect((await recoveryStarted).status()).toBe(202);

    const codeResponse = await recoveryContext.request.get(
      `/__test/email-code?identifier=${encodeURIComponent(account.email)}`,
    );
    expect(codeResponse.status()).toBe(200);
    const { code } = await codeResponse.json() as { code: string };
    await recoveryPage.getByLabel("Verification code").fill(code);
    const verificationResponse = recoveryPage.waitForResponse(
      response => response.request().method() === "POST"
        && new URL(response.url()).pathname === "/api/auth/password/recovery/verify",
      { timeout: 20_000 },
    );
    await recoveryPage.getByRole("button", { name: "Verify" }).click();
    const verification = await verificationResponse;
    expect(verification.status()).toBe(200);
    await expect(recoveryPage.getByLabel("New password", { exact: true })).toBeVisible();

    const browserStorage = await recoveryPage.evaluate(() => ({
      localKeys: Object.keys(localStorage),
      sessionKeys: Object.keys(sessionStorage),
      readableCookies: document.cookie,
    }));
    expect(JSON.stringify(browserStorage)).not.toMatch(/recoverygrant|passwordrecovery/i);

    const replacement = `Browser replacement passphrase ${account.suffix}`;
    await test.step("enter replacement password", async () => {
      await recoveryPage.getByLabel("New password", { exact: true }).fill(replacement);
      await recoveryPage.getByLabel("Confirm new password", { exact: true }).fill(replacement);
    });
    const reset = await test.step("submit replacement password", async () => {
      const resetResponse = recoveryPage.waitForResponse(
        response => response.request().method() === "POST"
          && new URL(response.url()).pathname === "/api/auth/password/reset",
        { timeout: 20_000 },
      );
      await recoveryPage.getByRole("button", { name: "Reset password" }).click();
      return resetResponse;
    });
    expect(reset.status()).toBe(204);
    expect(Object.keys(reset.request().postDataJSON()).sort()).toEqual([
      "confirmPassword", "newPassword",
    ]);
    await expect(recoveryPage.getByText("Your password has been reset.")).toBeVisible();

    const replayStatus = await recoveryPage.evaluate(async (password) => {
      const response = await fetch("/api/auth/password/reset", {
        method: "POST",
        credentials: "same-origin",
        headers: { "Content-Type": "application/json", "X-Weymela-Request": "1" },
        body: JSON.stringify({ newPassword: password, confirmPassword: password }),
      });
      return response.status;
    }, replacement);
    expect(replayStatus).toBe(400);
  } finally {
    await recoveryContext.close();
  }
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
  await expect(page.getByRole("heading", { name: "Welcome back" })).toBeVisible();
  await expect(second.getByRole("heading", { name: "Welcome back" })).toBeVisible();
  for (const width of [375, 390, 393, 430, 768, 1440]) {
    await page.setViewportSize({ width, height: width < 700 ? 844 : 900 });
    await layout(page);
    await pinTapTargets(page);
  }

  const session = await context.request.get("/api/session");
  expect(session.status()).toBe(423);
  const switchAttempt = await context.request.post("/api/session/switch-profile", {
    headers: { "X-Weymela-Request": "1", "Idempotency-Key": `locked-switch-${account.suffix}` },
    data: { role: "Customer", subjectId: "00000000-0000-0000-0000-000000000001", businessId: null },
  });
  expect(switchAttempt.status()).toBe(423);

  await fillPin(page, "PIN", "99999");
  const rejected = page.waitForResponse(response => response.request().method() === "POST"
    && new URL(response.url()).pathname === "/api/device/unlock");
  await page.getByRole("button", { name: "Unlock", exact: true }).click();
  expect((await rejected).status()).toBe(401);
  await expect(page.getByRole("alert")).toContainText("incorrect");

  await fillPin(page, "PIN", "01234");
  const unlocked = page.waitForResponse(response => response.request().method() === "POST"
    && new URL(response.url()).pathname === "/api/device/unlock");
  await page.getByRole("button", { name: "Unlock", exact: true }).click();
  expect((await unlocked).status()).toBe(200);
  await expect(page).toHaveURL(/\/customer\/offers/);
  await expect(page.getByRole("heading", { name: /Offers/ })).toBeVisible();
  await expect(second.getByRole("heading", { name: /Offers/ })).toBeVisible();
  await second.close();
});

test("forgot PIN on a locked recognized device uses verified email and replaces old credentials", async ({ page, context, browser }) => {
  page.setDefaultTimeout(8000);
  const account = await createVerifiedAccount(context);
  await activateCustomer(context, account);
  await open(page, "/customer/offers");
  const otherTab = await context.newPage();
  await open(otherTab, "/customer/offers");

  const oldContext = await browser.newContext({ baseURL: new URL(page.url()).origin });
  await oldContext.addCookies(await context.cookies());
  try {
    expect((await context.request.post("/__test/device-session/idle")).status()).toBe(204);
    await page.reload();
    await expect(page.getByRole("heading", { name: "Welcome back" })).toBeVisible();
    await page.getByRole("button", { name: "Forgot PIN" }).click();
    await expect(page.getByRole("heading", { name: "Reset your PIN" })).toBeVisible();
    for (const width of [375, 390, 393, 430, 768, 1440]) {
      await page.setViewportSize({ width, height: width < 700 ? 844 : 900 });
      await layout(page);
    }

    await page.getByLabel("Email address").fill(account.email);
    const started = page.waitForResponse(response => response.request().method() === "POST"
      && new URL(response.url()).pathname === "/api/auth/email/start");
    await page.getByRole("button", { name: "Continue" }).click();
    expect((await started).status()).toBe(202);
    await expect(page.getByText(/If the account is eligible/)).toBeVisible();
    const codeResponse = await context.request.get(`/__test/email-code?identifier=${encodeURIComponent(account.email)}`);
    expect(codeResponse.status()).toBe(200);
    const { code } = await codeResponse.json() as { code: string };

    await page.getByLabel("Verification code").fill("999999");
    await fillPin(page, "New PIN", "56789");
    await fillPin(page, "Confirm new PIN", "56789");
    const invalid = page.waitForResponse(response => response.request().method() === "POST"
      && new URL(response.url()).pathname === "/api/device/pin-recovery/complete");
    await page.getByRole("button", { name: "Recover device" }).click();
    expect((await invalid).status()).toBe(400);
    await expect(page.getByRole("alert")).toHaveText(/invalid or expired/);

    await page.getByLabel("Verification code").fill(code);
    const completed = page.waitForResponse(response => response.request().method() === "POST"
      && new URL(response.url()).pathname === "/api/device/pin-recovery/complete");
    await page.getByRole("button", { name: "Recover device" }).click();
    expect((await completed).status()).toBe(200);
    await expect(page).toHaveURL(/\/customer\/offers/);
    await expect(page.getByRole("heading", { name: /Offers/ })).toBeVisible();
    await expect(otherTab.getByRole("heading", { name: /Offers/ })).toBeVisible();

    const oldAccess = await oldContext.request.get("/api/device/access");
    expect(oldAccess.status()).toBe(200);
    await expect(oldAccess.json()).resolves.toMatchObject({ state: "FullAuthenticationRequired" });

    expect((await context.request.post("/__test/device-session/idle")).status()).toBe(204);
    await page.reload();
    await fillPin(page, "PIN", "01234");
    const oldPin = page.waitForResponse(response => response.request().method() === "POST"
      && new URL(response.url()).pathname === "/api/device/unlock");
    await page.getByRole("button", { name: "Unlock" }).click();
    expect((await oldPin).status()).toBe(401);
    await fillPin(page, "PIN", "56789");
    const newPin = page.waitForResponse(response => response.request().method() === "POST"
      && new URL(response.url()).pathname === "/api/device/unlock");
    await page.getByRole("button", { name: "Unlock" }).click();
    expect((await newPin).status()).toBe(200);
    await expect(page.getByRole("heading", { name: /Offers/ })).toBeVisible();
  } finally {
    await otherTab.close();
    await oldContext.close();
  }
});

test("recovery-required device can complete verified-email PIN recovery", async ({ page, context }) => {
  page.setDefaultTimeout(8000);
  const account = await createVerifiedAccount(context);
  await activateCustomer(context, account);
  expect((await context.request.post("/__test/device/recovery-required")).status()).toBe(204);
  await page.goto("/customer/offers");
  await expect(page.getByRole("heading", { name: "Welcome back" })).toBeVisible();
  await expect(page.getByRole("alert")).toContainText("Verify your email to reset your PIN");
  await page.getByRole("button", { name: "Forgot PIN" }).click();
  await page.getByLabel("Email address").fill(account.email);
  const started = page.waitForResponse(response => response.request().method() === "POST"
    && new URL(response.url()).pathname === "/api/auth/email/start");
  await page.getByRole("button", { name: "Continue" }).click();
  expect((await started).status()).toBe(202);
  const codeResponse = await context.request.get(`/__test/email-code?identifier=${encodeURIComponent(account.email)}`);
  const { code } = await codeResponse.json() as { code: string };
  await page.getByLabel("Verification code").fill(code);
  await fillPin(page, "New PIN", "24680");
  await fillPin(page, "Confirm new PIN", "24680");
  const completed = page.waitForResponse(response => response.request().method() === "POST"
    && new URL(response.url()).pathname === "/api/device/pin-recovery/complete");
  await page.getByRole("button", { name: "Recover device" }).click();
  expect((await completed).status()).toBe(200);
  await expect(page).toHaveURL(/\/customer\/offers/);
  await expect(page.getByRole("heading", { name: /Offers/ })).toBeVisible();
});

test("customer-to-creator choice uses a phase-safe purple shell", async ({ page, context }) => {
  page.setDefaultTimeout(8000);
  const account = await createVerifiedAccount(context);
  await activateCustomer(context, account);
  const signals = installRuntimeSignals(page);
  const marker = await buildMarker(context);
  await open(page, "/onboarding");
  await expect(page.locator("main h1")).toHaveText(/How do you want to use Weymela\?/);
  await expect(page.getByRole("button", { name: /^Add a Business/ })).toHaveCount(1);
  await expect(page.getByRole("button", { name: /^Customer — Already added/ })).toBeDisabled();
  await creatorChoiceDiagnostics(page, context, signals, marker);
  const creatorChoice = page.getByRole("button", { name: /^Become a Creator/ });
  await expect(creatorChoice).toHaveCount(1);
  await creatorChoice.click();
  await expect(page.getByRole("heading", { name: "Creator setup" })).toBeVisible();
  await expect(page.locator('[data-role-theme="creator"]')).toBeVisible();
  await expect(page.getByLabel("Public ID", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Submit for review" })).toHaveCount(0);
  for (const viewport of [{ width: 375, height: 667 }, { width: 390, height: 844 }, { width: 1366, height: 900 }]) {
    await page.setViewportSize(viewport);
    await layout(page);
  }
  await expect((await context.request.get("/api/session")).json()).resolves.toMatchObject({ role: "Customer" });
  await open(page, "/customer/offers");
});

test("public role shell keeps mobile navigation and profile actions tappable", async ({ page, context }) => {
  const account = await createVerifiedAccount(context);
  await activateCustomer(context, account);
  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, "/customer/offers");
  const navigation = page.getByRole("navigation", { name: "Mobile navigation" });
  await expect(navigation).toBeVisible();
  const more = navigation.getByRole("button", { name: "More navigation and profiles" });
  const target = await more.boundingBox();
  expect(target?.height).toBeGreaterThanOrEqual(44);
  await more.click();
  const menu = page.getByRole("dialog", { name: "Workspace menu" });
  await expect(menu).toBeVisible();
  await expect(menu.getByRole("link", { name: "Add a profile" })).toBeVisible();
  await expect(menu.getByLabel("Switch profile", { exact: true })).toBeVisible();
  const session = await (await context.request.get("/api/session")).json() as SessionUser;
  expect(await page.locator(".sidebar, .topbar, .nav-drawer, .mobile-role-nav").evaluateAll((elements, publicId) =>
    elements.some((element) => element.textContent?.includes(publicId)), session.publicId)).toBe(false);
  await page.keyboard.press("Escape");
  await expect(menu).not.toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
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

test("customer-to-business choice uses a phase-safe blue shell", async ({ page, context }) => {
  page.setDefaultTimeout(8000);
  const account = await createVerifiedAccount(context);
  await activateCustomer(context, account);
  await open(page, "/onboarding");
  await expect(page.locator("main h1")).toHaveText(/How do you want to use Weymela\?/);
  await expect(page.getByRole("button", { name: /^Become a Creator/ })).toHaveCount(1);
  await expect(page.getByRole("button", { name: /^Customer — Already added/ })).toBeDisabled();
  const businessChoice = page.getByRole("button", { name: /^Add a Business/ });
  await expect(businessChoice).toHaveCount(1);
  await businessChoice.click();
  await expect(page.getByRole("heading", { name: "Business setup" })).toBeVisible();
  await expect(page.locator('[data-role-theme="business"]')).toBeVisible();
  await expect(page.getByLabel("Public ID", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Submit for review" })).toHaveCount(0);
  for (const viewport of [{ width: 375, height: 667 }, { width: 390, height: 844 }, { width: 1366, height: 900 }]) {
    await page.setViewportSize(viewport);
    await layout(page);
  }
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

test("Platform and Operations Admin sessions keep their distinct authority", async ({ context }) => {
  await login(context, "admin");
  await expect((await context.request.get("/api/session")).json()).resolves.toMatchObject({ role: "PlatformAdmin" });
  expect((await context.request.get("/api/admin/operations")).status()).toBe(200);
  expect((await context.request.get("/api/admin/financial-settings")).status()).toBe(200);

  await login(context, "operations-admin");
  await expect((await context.request.get("/api/session")).json()).resolves.toMatchObject({ role: "OperationsAdmin" });
  expect((await context.request.get("/api/admin/operations")).status()).toBe(200);
  expect((await context.request.get("/api/admin/financial-settings")).status()).toBe(403);
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
  const select = page.locator("aside.sidebar").getByLabel("Switch profile", { exact: true });
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
  const password = `Browser passphrase ${suffix}`;
  const token = await runStep(steps, "signup-verify", () => emailCode(context, email, "Signup"), 15000);
  await runStep(steps, "firebase-session", () => establishFirebaseSession(context, token, "Customer"), 12000);
  await runStep(steps, "account-security", async () => {
    const secured = await context.request.post("/api/account/password-credential", {
      headers: { "X-Weymela-Request": "1" }, data: { phone, password, confirmPassword: password },
    });
    expect(secured.status()).toBe(204);
  }, 15000);
  await runStep(steps, "device-enrollment", () => enrollDevice(context, suffix), 15000);
  await runStep(steps, "onboarding-session", async () => expect((await context.request.get("/api/session", { timeout: 10000 })).json()).resolves.toMatchObject({ role: "Onboarding" }), 12000);

  try {
    await runStep(steps, "onboarding-open", () => open(page, "/onboarding"), 15000);
    await runStep(steps, "customer-choice", () => page.getByRole("button", { name: /^Use as Customer/ }).click({ timeout: 7000 }), 8000);
    await runStep(steps, "customer-form-visible", async () => {
      await expect(page.getByLabel("Preferred name", { exact: true })).toBeVisible({ timeout: 7000 });
      await expect(page.getByLabel("Public ID", { exact: true })).toHaveCount(0);
    }, 8000);
    await runStep(steps, "customer-form-fill", () => page.getByLabel("Preferred name", { exact: true }).fill(`Customer ${suffix}`, { timeout: 7000 }), 8000);
  } catch (error) {
    await captureCustomerFormFailure(page, context, signals, marker, steps, error);
    throw error;
  }
  await runStep(steps, "customer-legal-acceptance", () => page.getByRole("checkbox", { name: /I agree to the Terms of Service and acknowledge the Privacy Policy/ }).check({ timeout: 7000 }), 8000);
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

  await runStep(steps, "customer-sign-out", async () => {
    const response = await context.request.post("/api/session/sign-out", {
      headers: { "X-Weymela-Request": "1" },
    });
    expect(response.status()).toBe(204);
  }, 10000);

  // Full sign-in uses the normalized phone plus password and sends no email.
  const phoneToken = await runStep(steps, "phone-signin", async () => {
    const response = await context.request.post("/api/auth/password/sign-in", {
      headers: { "X-Weymela-Request": "1" }, data: { phone: `0${phone.slice(4)}`, password },
    });
    expect(response.status()).toBe(200);
    return (await response.json() as { customToken: string }).customToken;
  }, 15000);
  expect(phoneToken).toBe(token);
  await runStep(steps, "firebase-phone-session", () => establishFirebaseSession(context, phoneToken), 12000);
  await runStep(steps, "creator-onboarding-open", () => open(page, "/onboarding"), 15000);
  await expect(page.getByRole("button", { name: /^Customer — Already added/ })).toBeDisabled();
  await expect(page.getByRole("checkbox", { name: /Terms of Service/ })).toHaveCount(0);
  await creatorChoiceDiagnostics(page, context, signals, marker);
  await runStep(steps, "creator-choice", () => page.getByRole("button", { name: /^Become a Creator/ }).click({ timeout: 7000 }), 8000);
  await expect(page.getByRole("heading", { name: "Creator setup" })).toBeVisible();
  await expect(page.locator('[data-role-theme="creator"]')).toBeVisible();
  await expect(page.getByLabel("Public ID", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Back", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: /^Add a Business/ }).click();
  await expect(page.getByRole("heading", { name: "Business setup" })).toBeVisible();
  await expect(page.locator('[data-role-theme="business"]')).toBeVisible();
  await expect(page.getByLabel("Public ID", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Back", exact: true })).toHaveCount(0);
  await layout(page);
  await screenshot(page, "phase4a-role-themed-onboarding");
});
