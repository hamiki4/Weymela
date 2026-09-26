import { expect, type BrowserContext, type Page } from "@playwright/test";
import { readFileSync, mkdirSync } from "node:fs";
import { resolve } from "node:path";
const control = JSON.parse(
  readFileSync(resolve("../../.artifacts/browser-host.json"), "utf8"),
) as { accessKey: string };
export const evidence = resolve("../../.artifacts/phase6-screenshots");
mkdirSync(evidence, { recursive: true });
export async function login(context: BrowserContext, alias: string) {
  const response = await context.request.post("/api/development/session", {
    headers: { "X-Weymela-Request": "1" },
    data: { alias, accessKey: control.accessKey },
  });
  expect(response.status()).toBe(204);
}
export async function open(page: Page, path: string) {
  await page.goto(path);
  await expect(page.locator("main h1")).toBeVisible();
  await expect(
    page.getByRole("status", { name: "Loading workspace" }),
  ).toHaveCount(0);
  await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
  await expect(page.getByRole("alert")).toHaveCount(0);
}
export async function screenshot(page: Page, name: string) {
  await page.screenshot({
    path: `${evidence}/${name}.png`,
    fullPage: true,
    animations: "disabled",
  });
}
export async function layout(page: Page) {
  const issues = await page.evaluate(() => {
    const problems: string[] = [];
    if (document.documentElement.scrollWidth > window.innerWidth + 1) {
      const overflowing = [...document.querySelectorAll<HTMLElement>("body *")]
        .map((element) => ({ element, right: element.getBoundingClientRect().right }))
        .filter(({ right }) => right > window.innerWidth + 1)
        .sort((a, b) => b.right - a.right)[0];
      const detail = overflowing
        ? `${overflowing.element.tagName.toLowerCase()}.${typeof overflowing.element.className === "string" ? overflowing.element.className.replaceAll(" ", ".") : ""} right=${overflowing.right.toFixed(1)}px`
        : "unknown element";
      problems.push(`Document overflows horizontally on ${location.pathname}: document=${document.documentElement.scrollWidth}px viewport=${window.innerWidth}px; ${detail}`);
    }
    for (const element of document.querySelectorAll<HTMLElement>(
      "main input,main select,main textarea,main button,main .button,main h1,main h2,main th",
    )) {
      if (!element.checkVisibility()) continue;
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      if (
        !element.closest(".admin-table") &&
        rect.width < 18 &&
        element.textContent!.trim().length > 3
      )
        problems.push(`Compressed control: ${element.tagName}`);
      if (style.wordBreak === "break-all")
        problems.push("Single-letter wrapping permitted");
      if (element.matches("input,select,textarea") && rect.width > 850)
        problems.push("Oversized input");
    }
    return problems;
  });
  expect(issues).toEqual([]);
}
