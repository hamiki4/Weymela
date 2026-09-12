// Derive bitmap install icons from the approved existing vector, not a new visual design.
import { chromium } from "playwright";
import { readFile } from "node:fs/promises";
const svg = await readFile("public/icon.svg", "utf8");
const browser = await chromium.launch({ headless: true });
try {
  for (const size of [180, 192, 512]) {
    const page = await browser.newPage({ viewport: { width: size, height: size }, deviceScaleFactor: 1 });
    await page.setContent(`<html><body style="margin:0;background:#164d38"><img alt="" style="width:100%;height:100%" src="data:image/svg+xml;base64,${Buffer.from(svg).toString("base64")}"></body></html>`);
    await page.locator("img").evaluate(image => image.decode());
    await page.screenshot({ path: `public/icon-${size}.png` }); await page.close();
  }
} finally { await browser.close(); }
