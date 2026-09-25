import { expect, test, type Page } from "@playwright/test";

async function openPublicLanding(page: Page) {
  await page.route("**/api/auth/mode", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ development: false, personas: null }),
    });
  });
  await page.goto("/sign-in");
  await expect(
    page.getByRole("heading", { name: "Welcome to Weymela" }),
  ).toBeVisible();
  await expect(page.getByRole("link", { name: "Activate a Cashier account" })).toHaveCount(0);
}

for (const viewport of [
  { width: 375, height: 667 },
  { width: 390, height: 844 },
]) {
  test(`public landing keeps authentication primary at ${viewport.width}x${viewport.height}`, async ({
    page,
  }) => {
    await page.setViewportSize(viewport);
    await openPublicLanding(page);

    const create = page.getByRole("button", {
      name: "Create account",
      exact: true,
    });
    const signIn = page.getByRole("button", { name: "Sign in", exact: true });
    await expect(create).toBeVisible();
    await expect(signIn).toBeVisible();

    const measurements = await page.evaluate(() => {
      const hero = document.querySelector<HTMLElement>(".sign-in-story")!;
      const form = document.querySelector<HTMLElement>(".sign-in-form")!;
      const logo = document.querySelector<HTMLImageElement>(
        ".sign-in-story .brand-wordmark",
      )!;
      const brandMark = document.querySelector<HTMLImageElement>(
        ".sign-in-story .brand-mark",
      )!;
      const createButton = Array.from(
        document.querySelectorAll<HTMLButtonElement>("button"),
      ).find((button) => button.textContent?.trim() === "Create account")!;
      const signInButton = Array.from(
        document.querySelectorAll<HTMLButtonElement>("button"),
      ).find((button) => button.textContent?.trim() === "Sign in")!;
      const heroBox = hero.getBoundingClientRect();
      const formBox = form.getBoundingClientRect();
      const createBox = createButton.getBoundingClientRect();
      const signInBox = signInButton.getBoundingClientRect();
      const readable = Array.from(
        hero.querySelectorAll<HTMLElement>(".brand, .eyebrow, h1, p"),
      );
      const clipped = readable.some(
        (element) =>
          element.scrollWidth > element.clientWidth + 1 ||
          element.scrollHeight > element.clientHeight + 1,
      );
      const outsideHero = readable.some((element) => {
        const box = element.getBoundingClientRect();
        return (
          box.left < heroBox.left - 1 ||
          box.right > heroBox.right + 1 ||
          box.top < heroBox.top - 1 ||
          box.bottom > heroBox.bottom + 1
        );
      });
      return {
        occupancy: heroBox.height / window.innerHeight,
        actionsAboveFold:
          createBox.bottom <= window.innerHeight &&
          signInBox.bottom <= window.innerHeight,
        controlsUsable:
          createBox.height >= 44 &&
          signInBox.height >= 44 &&
          createBox.width >= 44 &&
          signInBox.width >= 44,
        horizontalOverflow:
          document.documentElement.scrollWidth > window.innerWidth + 1,
        sectionsOverlap: heroBox.bottom > formBox.top + 1,
        clipped,
        outsideHero,
        approvedLogoLoaded:
          logo.currentSrc.endsWith("/brand/weymela-wordmark.png") &&
          brandMark.currentSrc.endsWith("/brand/weymela-mark.png") &&
          logo.naturalWidth > 0 &&
          brandMark.naturalWidth > 0,
        brandedText: readable.map((element) => element.textContent?.trim()),
      };
    });
    console.log(
      `landing ${viewport.width}x${viewport.height} hero occupancy ${(measurements.occupancy * 100).toFixed(1)}%`,
    );

    expect(measurements.occupancy).toBeGreaterThan(0);
    expect(measurements.occupancy).toBeLessThan(0.25);
    expect(measurements.actionsAboveFold).toBe(true);
    expect(measurements.controlsUsable).toBe(true);
    expect(measurements.horizontalOverflow).toBe(false);
    expect(measurements.sectionsOverlap).toBe(false);
    expect(measurements.clipped).toBe(false);
    expect(measurements.outsideHero).toBe(false);
    expect(measurements.approvedLogoLoaded).toBe(true);
    const branding = measurements.brandedText.join(" ").replace(/\s+/g, " ");
    expect(branding).not.toContain("Good stories. Real connections.");
    expect(branding).not.toContain("A place to grow together.");
  });
}

test("public landing remains focused on desktop", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPublicLanding(page);

  const measurements = await page.evaluate(() => {
    return {
      horizontalOverflow:
        document.documentElement.scrollWidth > window.innerWidth + 1,
    };
  });
  expect(measurements.horizontalOverflow).toBe(false);
  await expect(
    page.getByRole("button", { name: "Create account", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Sign in", exact: true }),
  ).toBeVisible();
});

test("production branding assets are transparent, compact, and replace the old icon", async ({
  page,
}) => {
  await page.goto("/sign-in");
  await expect(page.getByRole("img", { name: "Weymela" })).toBeVisible();
  const head = await page.locator("head").innerHTML();
  expect(head).toContain("/favicon-32.png");
  expect(head).toContain("/favicon-16.png");
  expect(head).not.toContain("/icon.svg");
  const transparency = await page.evaluate(async () => {
    const inspect = async (source: string) => {
      const blob = await (await fetch(source)).blob();
      const image = await createImageBitmap(blob);
      const canvas = document.createElement("canvas");
      canvas.width = image.width;
      canvas.height = image.height;
      const context = canvas.getContext("2d")!;
      context.drawImage(image, 0, 0);
      const corners = [
        context.getImageData(0, 0, 1, 1).data[3],
        context.getImageData(image.width - 1, 0, 1, 1).data[3],
        context.getImageData(0, image.height - 1, 1, 1).data[3],
        context.getImageData(image.width - 1, image.height - 1, 1, 1).data[3],
      ];
      image.close();
      return {
        width: canvas.width,
        height: canvas.height,
        corners: Array.from(corners),
      };
    };
    return {
      mark: await inspect("/brand/weymela-mark.png"),
      favicon: await inspect("/favicon-32.png"),
    };
  });
  expect(transparency.mark).toMatchObject({
    width: 512,
    height: 512,
    corners: [0, 0, 0, 0],
  });
  expect(transparency.favicon).toMatchObject({
    width: 32,
    height: 32,
    corners: [0, 0, 0, 0],
  });
});
