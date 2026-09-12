import { defineConfig } from "@playwright/test";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
const control = JSON.parse(
  readFileSync(resolve("../../.artifacts/browser-host.json"), "utf8"),
) as { url: string };
if (!control.url.startsWith("http://127.0.0.1:"))
  throw new Error("Only the isolated loopback V3 browser host is permitted.");
export default defineConfig({
  testDir: "./e2e",
  timeout: 90000,
  expect: { timeout: 12000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [
    ["list"],
    ["json", { outputFile: "../../.artifacts/phase6-e2e-results.json" }],
  ],
  outputDir: "../../.artifacts/playwright",
  use: {
    baseURL: control.url,
    headless: true,
    trace: "off",
    video: "off",
    screenshot: "off",
  },
});
