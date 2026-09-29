import { expect, test } from "@playwright/test";
import { layout, login, open } from "./helpers";

test("Add Profile forms, review, bell routes and switching remain usable on phones", async ({ page, context, browser }) => {
  const suffix = Date.now().toString();
  const email = `add-profile-${suffix}@example.com`;
  const phone = `+2519${suffix.slice(-8)}`;
  const started = await context.request.post("/api/auth/email/start", { headers: { "X-Weymela-Request": "1" },
    data: { identifier: email, purpose: "Signup" } });
  expect(started.status()).toBe(202);
  const codeResponse = await context.request.get(`/__test/email-code?identifier=${encodeURIComponent(email)}`);
  const { code } = await codeResponse.json() as { code: string };
  const verified = await context.request.post("/api/auth/email/verify", { headers: { "X-Weymela-Request": "1" },
    data: { identifier: email, purpose: "Signup", code } });
  expect(verified.status()).toBe(200);
  const { customToken } = await verified.json() as { customToken: string };
  const signedIn = await context.request.post("/api/auth/firebase/session", { headers: { "X-Weymela-Request": "1" },
    data: { idToken: customToken } });
  expect(signedIn.status()).toBe(204);
  const credential = await context.request.post("/api/account/password-credential", { headers: { "X-Weymela-Request": "1" },
    data: { phone, password: `Browser passphrase ${suffix}`, confirmPassword: `Browser passphrase ${suffix}` } });
  expect(credential.status()).toBe(204);
  const device = await context.request.post("/api/device/enrollment", { headers: { "X-Weymela-Request": "1", "Idempotency-Key": `device-${suffix}` },
    data: { pin: "01234", confirmPin: "01234" } });
  expect(device.status()).toBe(200);
  const legal = await (await context.request.get("/api/onboarding/legal")).json() as { documents: { kind: string; documentId: string; contentHash: string }[] };
  const terms = legal.documents.find(item => item.kind === "TermsOfService")!;
  const privacy = legal.documents.find(item => item.kind === "PrivacyPolicy")!;
  const customer = await context.request.post("/api/onboarding/profile", { headers: { "X-Weymela-Request": "1", "Idempotency-Key": `customer-${suffix}` },
    data: { role: "Customer", displayName: "Hana", accountLegal: {
      termsOfService: { documentId: terms.documentId, contentHash: terms.contentHash, accepted: true },
      privacyPolicy: { documentId: privacy.documentId, contentHash: privacy.contentHash, accepted: true },
    } } });
  expect(customer.status()).toBe(200);
  for (const width of [320, 360, 375, 390, 430]) {
    await page.setViewportSize({ width, height: 844 });
    await open(page, "/onboarding");
    await expect(page.getByRole("button", { name: /^Customer — Already added/ })).toBeDisabled();
    await page.getByRole("button", { name: /^Become a Creator/ }).click();
    await expect(page.getByRole("heading", { name: "Creator setup" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Submit for Review" })).toBeDisabled();
    await page.getByRole("button", { name: "Add TikTok" }).click();
    await page.getByLabel("TikTok profile URL").fill("https://www.tiktok.com/@hana");
    await layout(page);
    await page.getByRole("button", { name: /^Add a Business/ }).click();
    await expect(page.getByRole("heading", { name: "Business setup" })).toBeVisible();
    await expect(page.getByText("Payment Reference")).toHaveCount(0);
    await layout(page);
  }

  await page.getByLabel("Business name").fill("Hana Cafe");
  await page.getByLabel("Business type (optional)").fill("Restaurant");
  await page.getByRole("button", { name: "Submit for Review" }).click();
  await expect(page.getByText("Your Business profile is waiting for approval.")).toBeVisible();
  for (const width of [320, 360, 375, 390, 430]) {
    await page.setViewportSize({ width, height: 844 });
    await layout(page);
  }
  await page.getByRole("button", { name: /^Become a Creator/ }).click();
  await page.getByLabel("Creator name").fill("Hana");
  await page.getByRole("button", { name: "Add Instagram" }).click();
  await page.getByLabel("Instagram profile URL").fill("https://www.instagram.com/hana/");
  await page.getByRole("button", { name: "Submit for Review" }).click();
  await expect(page.getByText("Your Creator profile is waiting for approval.")).toBeVisible();
  for (const width of [320, 360, 375, 390, 430]) {
    await page.setViewportSize({ width, height: 844 });
    await layout(page);
  }

  const adminContext = await browser.newContext({ baseURL: new URL(page.url()).origin, viewport: { width: 320, height: 844 } });
  try {
    await login(adminContext, "operations-admin");
    const admin = await adminContext.newPage();
    await expect.poll(async () => {
      const response = await adminContext.request.get("/api/notifications");
      const body = await response.json() as { items: { title: string; route: string }[] };
      return body.items.filter(item => item.title === "Profile awaiting review" && item.route === "/admin/role-enrollments").length;
    }).toBe(2);
    await open(admin, "/admin/operations");
    await admin.getByRole("link", { name: "Your notifications" }).click();
    await expect(admin).toHaveURL(/\/notifications/);
    await expect(admin.getByRole("heading", { name: "Profile awaiting review" })).toHaveCount(2);
    await admin.locator(".notification-item").filter({ hasText: "Creator application" }).getByRole("link", { name: "Open" }).click();
    await expect(admin).toHaveURL(/\/admin\/role-enrollments/);
    await expect(admin.getByText("https://www.instagram.com/hana/", { exact: true })).toHaveCount(0);
    const creator = admin.locator(".admin-profile-request").filter({ hasText: "Creator · Hana" });
    await expect(creator.getByText("Instagram")).toBeVisible();
    await expect(creator.getByRole("link", { name: "View profile" })).toHaveAttribute("href", "https://www.instagram.com/hana");
    for (const width of [320, 360, 375, 390, 430]) {
      await admin.setViewportSize({ width, height: 844 });
      await layout(admin);
    }
    await creator.getByRole("button", { name: "Approve" }).click();
    const business = admin.locator(".admin-profile-request").filter({ hasText: "Business · Hana Cafe" });
    await business.getByRole("button", { name: "Reject" }).click();
    await business.getByLabel("Reason for rejection").fill("More business details needed");
    await business.getByRole("button", { name: "Confirm rejection" }).click();
    await expect(admin.locator(".admin-profile-request")).toHaveCount(0);
    await layout(admin);
  } finally {
    await adminContext.close();
  }

  await expect.poll(async () => {
    const response = await context.request.get("/api/notifications");
    const body = await response.json() as { items: { title: string; route: string }[] };
    return body.items.filter(item => item.title.includes("profile") && item.route === "/onboarding").length;
  }).toBe(2);
  await open(page, "/customer/offers");
  await page.getByRole("link", { name: "Your notifications" }).click();
  await expect(page).toHaveURL(/\/notifications/);
  await expect(page.getByRole("heading", { name: "Creator profile approved" })).toBeVisible();
  await expect(page.getByText("More business details needed")).toBeVisible();
  await page.locator(".notification-item").filter({ hasText: "Creator profile approved" }).getByRole("link", { name: "Open" }).click();
  await expect(page).toHaveURL(/\/onboarding/);
  await expect(page.getByRole("button", { name: /^Creator — Already added/ })).toBeDisabled();
  await expect(page.getByText("More business details needed")).toBeVisible();
  await expect(page.getByRole("button", { name: /^Add a Business/ })).toBeEnabled();
  const session = await (await context.request.get("/api/session")).json() as { profiles: { role: string }[] };
  expect(session.profiles.map(profile => profile.role)).toContain("Creator");
  expect(session.profiles.map(profile => profile.role)).not.toContain("Business");
  await layout(page);
  await open(page, "/customer/offers");
  await page.getByRole("button", { name: "Open Settings" }).click();
  const switcher = page.getByRole("dialog", { name: "Settings" }).getByLabel("Switch profile");
  await expect(switcher.locator("option")).toHaveCount(2);
  await expect(switcher.locator("option").filter({ hasText: "Creator" })).toHaveCount(1);
  await switcher.selectOption({ label: "Hana — Creator" });
  await expect(page).toHaveURL(/\/creator/);
});
