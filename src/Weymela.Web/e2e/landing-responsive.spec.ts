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
      const mark = document.querySelector<HTMLElement>(".story-mark")!;
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
        markIgnoresPointer: getComputedStyle(mark).pointerEvents === "none",
        brandedText: readable.map((element) => element.textContent?.trim()),
      };
    });
    console.log(
      `landing ${viewport.width}x${viewport.height} hero occupancy ${(measurements.occupancy * 100).toFixed(1)}%`,
    );

    expect(measurements.occupancy).toBeGreaterThanOrEqual(0.25);
    expect(measurements.occupancy).toBeLessThanOrEqual(0.31);
    expect(measurements.actionsAboveFold).toBe(true);
    expect(measurements.controlsUsable).toBe(true);
    expect(measurements.horizontalOverflow).toBe(false);
    expect(measurements.sectionsOverlap).toBe(false);
    expect(measurements.clipped).toBe(false);
    expect(measurements.outsideHero).toBe(false);
    expect(measurements.markIgnoresPointer).toBe(true);
    const branding = measurements.brandedText.join(" ").replace(/\s+/g, " ");
    expect(branding).toContain("Good stories. Real connections.");
    expect(branding.replaceAll(" ", "")).toContain("Aplacetogrowtogether.");
    expect(branding).toContain(
      "Bring your business, creativity and community closer.",
    );
  });
}

test("public landing preserves the desktop split presentation", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPublicLanding(page);

  const measurements = await page.evaluate(() => {
    const hero = document.querySelector<HTMLElement>(".sign-in-story")!;
    const form = document.querySelector<HTMLElement>(".sign-in-form")!;
    const title = hero.querySelector<HTMLElement>("h1")!;
    const heroBox = hero.getBoundingClientRect();
    const formBox = form.getBoundingClientRect();
    return {
      heroWidthRatio: heroBox.width / window.innerWidth,
      heroHeightRatio: heroBox.height / window.innerHeight,
      formStartsAfterHero: formBox.left >= heroBox.right - 1,
      titleSize: Number.parseFloat(getComputedStyle(title).fontSize),
      horizontalOverflow:
        document.documentElement.scrollWidth > window.innerWidth + 1,
    };
  });

  expect(measurements.heroWidthRatio).toBeGreaterThanOrEqual(0.49);
  expect(measurements.heroWidthRatio).toBeLessThanOrEqual(0.51);
  expect(measurements.heroHeightRatio).toBeGreaterThanOrEqual(0.99);
  expect(measurements.formStartsAfterHero).toBe(true);
  expect(measurements.titleSize).toBeGreaterThanOrEqual(70);
  expect(measurements.horizontalOverflow).toBe(false);
  await expect(
    page.getByRole("button", { name: "Create account", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Sign in", exact: true }),
  ).toBeVisible();
});
