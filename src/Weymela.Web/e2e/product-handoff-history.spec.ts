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
