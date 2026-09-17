import { expect, test, type BrowserContext } from "@playwright/test";
import { login } from "./helpers";

test.use({ serviceWorkers: "block" });

type ProductCase = {
  alias: string;
  role: "Customer" | "Creator" | "Business" | "PlatformAdmin";
  entry: string;
  destination: string;
};

const products: ProductCase[] = [
  { alias: "customer", role: "Customer", entry: "/customer/offers", destination: "/shopper" },
  { alias: "creator", role: "Creator", entry: "/creator", destination: "/creator" },
  { alias: "business", role: "Business", entry: "/business", destination: "/business" },
  { alias: "admin", role: "PlatformAdmin", entry: "/admin", destination: "/admin" },
];

async function installProductBoundary(context: BrowserContext, product: ProductCase) {
  let ready = false;
  const plumbingRequests: { path: string; resourceType: string; navigation: boolean }[] = [];

  await context.route("**/api/integration/product/configuration", async route => {
    await route.fulfill({ json: {
      enabled: true,
      beginUrl: "/api/v1/integration/v3/begin",
      callbackId: "pilot-v3-web",
    }});
  });
  await context.route("**/api/v1/integration/v3/begin", async route => {
    const request = route.request();
    plumbingRequests.push({ path: new URL(request.url()).pathname,
      resourceType: request.resourceType(), navigation: request.isNavigationRequest() });
    expect(request.method()).toBe("POST");
    await expect(request.headerValue("x-weymela-product-request")).resolves.toBe("1");
    expect(request.postData()).toBe(`role=${product.role}&purpose=EXISTING_WORKSPACE`);
    await route.fulfill({ json: { state: "clean-history-state" } });
  });
  await context.route("**/api/integration/product/handoff", async route => {
    expect(route.request().method()).toBe("POST");
    const body = route.request().postDataJSON();
    expect(body).toMatchObject({ role: product.role, purpose: "EXISTING_WORKSPACE",
      callbackId: "pilot-v3-web", state: "clean-history-state" });
    await route.fulfill({ json: {
      code: "single-use-authorization-code",
      state: "clean-history-state",
      callbackUrl: "/api/v1/integration/v3/callback",
      expiresAtUtc: "2099-01-01T00:00:00Z",
    }});
  });
  await context.route("**/api/v1/integration/v3/callback", async route => {
    const request = route.request();
    plumbingRequests.push({ path: new URL(request.url()).pathname,
      resourceType: request.resourceType(), navigation: request.isNavigationRequest() });
    expect(request.method()).toBe("POST");
    await expect(request.headerValue("x-weymela-product-request")).resolves.toBe("1");
    ready = true;
    await route.fulfill({ json: { destination: product.destination } });
  });
  await context.route(`**${product.destination}`, async route => {
    if (!ready || !route.request().isNavigationRequest()) {
      await route.continue();
      return;
    }
    await route.fulfill({ contentType: "text/html", body:
      `<!doctype html><html><body><main><h1>${product.role} product</h1></main></body></html>` });
  });

  return plumbingRequests;
}

async function installProfileOnboardingBoundary(context: BrowserContext, role: "Creator" | "Business") {
  const destination = `/onboarding/${role.toLowerCase()}`;
  let ready = false;
  await context.route("**/api/onboarding/status", route => route.fulfill({ json: { profiles: [] } }));
  await context.route("**/api/onboarding/legal", route => route.fulfill({ json: {
    available: true, current: true, documents: [],
  }}));
  await context.route("**/api/integration/product/configuration", route => route.fulfill({ json: {
    enabled: true, beginUrl: "/api/v1/integration/v3/begin", callbackId: "pilot-v3-web",
  }}));
  await context.route("**/api/v1/integration/v3/begin", async route => {
    expect(route.request().postData()).toBe(`role=${role}&purpose=PROFILE_ONBOARDING`);
    await route.fulfill({ json: { state: "profile-onboarding-state" } });
  });
  await context.route("**/api/integration/product/handoff", async route => {
    expect(route.request().postDataJSON()).toMatchObject({ role, purpose: "PROFILE_ONBOARDING",
      callbackId: "pilot-v3-web", state: "profile-onboarding-state" });
    await route.fulfill({ json: { code: "single-use-profile-code", state: "profile-onboarding-state",
      callbackUrl: "/api/v1/integration/v3/callback", expiresAtUtc: "2099-01-01T00:00:00Z" } });
  });
  await context.route("**/api/v1/integration/v3/callback", async route => {
    ready = true;
    await route.fulfill({ json: { destination } });
  });
  await context.route(`**${destination}`, async route => {
    if (!ready || !route.request().isNavigationRequest()) return route.continue();
    await route.fulfill({ contentType: "text/html", body:
      `<!doctype html><html><body><main><h1>${role} Sign Up</h1></main></body></html>` });
  });
}

for (const product of products) {
  test(`${product.role} lands directly with clean Back and Refresh history`, async ({ context, page }) => {
    await login(context, product.alias);
    const plumbing = await installProductBoundary(context, product);
    const navigations: string[] = [];
    page.on("framenavigated", frame => {
      if (frame === page.mainFrame()) navigations.push(new URL(frame.url()).pathname);
    });

    await page.goto("/legal/terms-of-service");
    await expect(page.locator("main h1")).toBeVisible();
    await page.goto(product.entry);
    await expect(page).toHaveURL(new RegExp(`${product.destination.replace("/", "\\/")}$`));
    await expect(page.getByRole("heading", { name: `${product.role} product` })).toBeVisible();

    expect(plumbing).toHaveLength(2);
    expect(plumbing.every(request => request.resourceType === "fetch" && !request.navigation)).toBe(true);
    expect(navigations.some(path => path === "/product-handoff"
      || path.endsWith("/integration/v3/begin") || path.endsWith("/integration/v3/callback"))).toBe(false);

    await page.reload();
    await expect(page).toHaveURL(new RegExp(`${product.destination.replace("/", "\\/")}$`));
    await expect(page.getByRole("heading", { name: `${product.role} product` })).toBeVisible();

    await page.goBack();
    await expect(page).toHaveURL(/\/legal\/terms-of-service$/);
    expect(new URL(page.url()).pathname).not.toMatch(/product-handoff|integration\/v3\/(begin|callback)/);
  });
}

for (const role of ["Creator", "Business"] as const) {
  test(`${role} detailed registration keeps profiles as the clean Back destination`, async ({ context, page }) => {
    await login(context, "customer");
    await installProfileOnboardingBoundary(context, role);

    await page.goto("/onboarding");
    await page.getByRole("button", { name: new RegExp(`^${role} —`) }).click();
    await page.getByRole("button", { name: "Continue", exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`${role.toLowerCase()}$`));
    await expect(page.getByRole("heading", { name: `${role} Sign Up` })).toBeVisible();

    await page.goBack();
    await expect(page).toHaveURL(/\/onboarding$/);
    await expect(page.getByRole("heading", { name: "How do you want to use Weymela?" })).toBeVisible();
  });

  test(`direct V3 ${role} route without that selected profile recovers to profiles`, async ({ context, page }) => {
    await login(context, "customer");
    await context.route("**/api/integration/product/configuration", route => route.fulfill({ json: {
      enabled: true, beginUrl: "/api/v1/integration/v3/begin", callbackId: "pilot-v3-web",
    }}));

    await page.goto(`/${role.toLowerCase()}`);
    await expect(page).toHaveURL(/\/onboarding$/);
    await expect(page.getByRole("heading", { name: "How do you want to use Weymela?" })).toBeVisible();
    await expect(page.getByText("Request failed (403)")).toHaveCount(0);
  });
}

for (const role of ["Creator", "Business"] as const) {
  test(`rejected ${role} enrollment is shown as a lifecycle state instead of a new-profile action`, async ({ context, page }) => {
    await login(context, "customer");
    await context.route("**/api/integration/product/configuration", route => route.fulfill({ json: {
      enabled: true, beginUrl: "/api/v1/integration/v3/begin", callbackId: "pilot-v3-web",
    }}));
    await context.route("**/api/onboarding/status", route => route.fulfill({ json: { profiles: [{
      id: `rejected-${role.toLowerCase()}`, role, status: "Rejected", displayName: `Rejected ${role}`,
      submittedAtUtc: "2026-09-17T00:00:00Z", decisionReason: "Not approved",
    }] } }));

    await page.goto("/onboarding");
    const choice = page.getByRole("button", { name: `${role} — Not approved — contact support` });
    await expect(choice).toBeDisabled();
    await expect(page.getByRole("button", { name: role === "Creator" ? /^Creator — Become a Creator/ : /^Business — Add a Business/ })).toHaveCount(0);
  });
}
