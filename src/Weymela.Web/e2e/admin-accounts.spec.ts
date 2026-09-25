import { test, expect } from "@playwright/test";
import { layout, login, open } from "./helpers";

const id = (number: number) => `00000000-0000-4000-8000-${number.toString().padStart(12, "0")}`;

test("Platform Admin has role-specific account areas and can preauthorize an Admin", async ({ page, context }) => {
  await login(context, "admin");
  await open(page, "/admin");
  await expect(page.locator(".topbar-identity")).not.toBeEmpty();
  await expect(page.locator(".topbar-identity")).not.toContainText("@");
  await expect(page.getByRole("link", { name: "Audit", exact: true })).toHaveCount(0);
  for (const [path, title, action] of [
    ["/admin/customers", "Customers", "Customer"], ["/admin/creators", "Creators", "Creator"],
    ["/admin/businesses", "Businesses", "Business"], ["/admin/admins", "Admins", "Admin"],
  ]) {
    await open(page, path);
    await expect(page.getByRole("heading", { name: title, exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: `+ Create ${action}` })).toBeVisible();
    await expect(page.getByRole("button", { name: "View As" })).toHaveCount(0);
  }
  await page.getByRole("button", { name: "+ Create Admin" }).click();
  await page.getByLabel("Admin type").selectOption("PlatformAdmin");
  await page.getByLabel("Email (required for a new identity)").fill(`browser-admin-${Date.now()}@example.com`);
  await page.getByLabel("Display name").fill("Browser Platform Admin");
  await page.getByLabel("Reason").fill("Browser security authority test");
  await page.getByRole("button", { name: "Create preauthorization" }).click();
  await expect(page.getByRole("status")).toContainText("Preauthorization created");
  await expect(page.locator("main")).not.toContainText(/password hash|pin verifier|firebase token|activation code/i);
  await layout(page);
});

test("recipient activates a Platform Admin-created Customer with own credentials and consent", async ({ page, context }) => {
  await login(context, "admin");
  await open(page, "/admin/customers");
  const suffix = Date.now().toString();
  const email = `invited-${suffix}@example.com`;
  const phone = `+2519${suffix.slice(-8)}`;
  const password = `Recipient passphrase ${suffix}`;
  await page.getByRole("button", { name: "+ Create Customer" }).click();
  await page.getByLabel("Email (required for a new identity)").fill(email);
  await page.getByLabel("Display name").fill("Invited Customer");
  await page.getByRole("button", { name: "Create preauthorization" }).click();
  await expect(page.getByRole("status")).toContainText("Preauthorization created");
  const invitation = await context.request.get(`/__test/email-code?identifier=${encodeURIComponent(email)}`);
  expect(invitation.status()).toBe(200);
  const { code: activationSecret } = await invitation.json() as { code: string };
  expect(activationSecret).toMatch(/^[0-9a-f]{32}-[A-Za-z0-9_-]{43}$/i);
  expect((await context.request.post("/api/session/sign-out", { headers: { "X-Weymela-Request": "1" } })).status()).toBe(204);

  const start = await context.request.post("/api/auth/email/start", {
    headers: { "X-Weymela-Request": "1" }, data: { identifier: email, phone: null, purpose: "Signup" },
  });
  expect(start.status()).toBe(202);
  const verification = await context.request.get(`/__test/email-code?identifier=${encodeURIComponent(email)}`);
  expect(verification.status()).toBe(200);
  const { code } = await verification.json() as { code: string };
  const verified = await context.request.post("/api/auth/email/verify", {
    headers: { "X-Weymela-Request": "1" }, data: { identifier: email, purpose: "Signup", code },
  });
  expect(verified.status()).toBe(200);
  const { customToken } = await verified.json() as { customToken: string };
  const session = await context.request.post("/api/auth/firebase/session", {
    headers: { "X-Weymela-Request": "1" }, data: { idToken: customToken },
  });
  expect(session.status()).toBe(204);
  const secured = await context.request.post("/api/account/password-credential", {
    headers: { "X-Weymela-Request": "1" }, data: { phone, password, confirmPassword: password },
  });
  expect(secured.status()).toBe(204);
  const device = await context.request.post("/api/device/enrollment", {
    headers: { "X-Weymela-Request": "1", "Idempotency-Key": `invited-device-${suffix}` },
    data: { pin: "01234", confirmPin: "01234" },
  });
  expect(device.status()).toBe(200);

  await open(page, "/account/activate");
  await page.getByLabel("Activation code").fill(activationSecret);
  await page.getByRole("checkbox", { name: /Terms of Service/ }).check();
  await page.getByRole("button", { name: "Activate account" }).click();
  await expect(page.getByRole("status")).toContainText("Your account is active");
  expect((await context.request.post("/api/session/sign-out", { headers: { "X-Weymela-Request": "1" } })).status()).toBe(204);
  const passwordSignIn = await context.request.post("/api/auth/password/sign-in", {
    headers: { "X-Weymela-Request": "1" }, data: { phone, password },
  });
  expect(passwordSignIn.status()).toBe(200);
  const loginToken = (await passwordSignIn.json() as { customToken: string }).customToken;
  expect((await context.request.post("/api/auth/firebase/session", {
    headers: { "X-Weymela-Request": "1" }, data: { idToken: loginToken },
  })).status()).toBe(204);
  expect((await context.request.get("/api/session")).status()).toBe(200);
  await expect((await context.request.get("/api/session")).json()).resolves.toMatchObject({ role: "Customer" });
});

test("Operations Admin cannot create accounts or enter Platform Admin account areas", async ({ page, context }) => {
  await login(context, "operations-admin");
  expect((await context.request.get("/api/admin/accounts")).status()).toBe(403);
  expect((await context.request.get(`/api/admin/accounts/${id(1)}`)).status()).toBe(403);
  expect((await context.request.get(`/api/admin/accounts/businesses/${id(2)}/cashiers`)).status()).toBe(403);
  await page.goto("/admin/admins");
  await expect(page).toHaveURL(/\/unauthorized$/);
  await open(page, "/admin/operations");
  await expect(page.getByRole("link", { name: "Financial Settings" })).toHaveCount(0);
  await expect(page.getByRole("link", { name: "Platform Revenue" })).toHaveCount(0);
  await layout(page);
});

test("legacy View As cookie does not change the authenticated actor", async ({ page, context }) => {
  await login(context, "admin");
  await open(page, "/admin");
  await context.addCookies([{ name: "WeymelaV3.SupportSession", value: id(7), url: new URL(page.url()).origin }]);
  await open(page, "/admin");
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  await expect(page.locator("body")).not.toContainText("ADMIN VIEW MODE");
  await expect(page.getByRole("link", { name: "Admins" })).toBeVisible();
});

test("Platform Admin manages Customer lock and deactivation from the account page", async ({ page, context }) => {
  await login(context, "admin");
  const target = id(7);
  try {
    await open(page, `/admin/accounts/${target}?role=Customer&mode=manage`);
    const reason = page.getByLabel("Reason for lifecycle action");
    await reason.fill("Browser account review");
    await page.getByRole("button", { name: "Lock", exact: true }).click();
    await expect(page.getByRole("button", { name: "Unlock", exact: true })).toBeVisible();
    await reason.fill("Browser review complete");
    await page.getByRole("button", { name: "Unlock", exact: true }).click();
    await expect(page.getByRole("button", { name: "Lock", exact: true })).toBeVisible();
    await reason.fill("Browser temporary deactivation");
    await page.getByRole("button", { name: "Deactivate", exact: true }).click();
    await expect(page.getByRole("button", { name: "Reactivate", exact: true })).toBeVisible();
    await reason.fill("Browser account restored");
    await page.getByRole("button", { name: "Reactivate", exact: true }).click();
    await expect(page.getByRole("button", { name: "Lock", exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Close Account", exact: true })).toBeDisabled();
  } finally {
    const response = await context.request.get(`/api/admin/accounts/${target}`);
    if (response.ok()) {
      const detail = await response.json() as { account: { status: string } };
      const action = detail.account.status === "Suspended" ? "unlock"
        : detail.account.status === "Disabled" ? "reactivate" : null;
      if (action) await context.request.post(`/api/admin/accounts/${target}/lifecycle`, {
        headers: { "X-Weymela-Request": "1", "Idempotency-Key": `browser-cleanup-${Date.now()}` },
        data: { action, reason: "Restore BrowserHost fixture after account-management test" },
      });
    }
  }
});
