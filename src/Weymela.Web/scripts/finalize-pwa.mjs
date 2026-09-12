import { createHash } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
const build = createHash("sha256").update(await readFile("dist/index.html")).digest("hex").slice(0, 20);
const worker = await readFile("dist/sw.js", "utf8");
if (!worker.includes("__BUILD_ID__")) throw new Error("Missing PWA release marker");
await writeFile("dist/sw.js", worker.replaceAll("__BUILD_ID__", build));
