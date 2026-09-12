// Read-only package/license metadata inventory. This is not legal advice or a replacement for release SBOM signing.
import { readFile, readdir, writeFile, mkdir } from "node:fs/promises";
import { join } from "node:path";
const root = process.cwd(); const nuget = process.env.V3_NUGET_PACKAGES || "/root/.nuget/packages";
const packages = new Map();
for (const group of ["src", "tests"]) for (const directory of await readdir(join(root, group), { withFileTypes: true })) {
  if (!directory.isDirectory()) continue;
  let assets; try { assets = JSON.parse(await readFile(join(root, group, directory.name, "obj/project.assets.json"), "utf8")); } catch { continue; }
  for (const [key, value] of Object.entries(assets.libraries)) if (value.type === "package" && !packages.has(key)) {
    const [name, version] = key.split("/"); let license = "REVIEW_REQUIRED";
    try { const xml = await readFile(join(nuget, name.toLowerCase(), version.toLowerCase(), `${name.toLowerCase()}.nuspec`), "utf8"); license = xml.match(/<license[^>]*>([^<]+)<\/license>/)?.[1] || xml.match(/<licenseUrl>([^<]+)<\/licenseUrl>/)?.[1] || license; } catch { }
    packages.set(key, { ecosystem: "NuGet", name, version, license });
  }
}
const lock = JSON.parse(await readFile("src/Weymela.Web/package-lock.json", "utf8"));
for (const [path, value] of Object.entries(lock.packages)) if (path) packages.set(path, { ecosystem: "npm", name: path.replace(/^.*node_modules\//, ""), version: value.version, license: value.license || "REVIEW_REQUIRED", development: !!value.dev });
const items = [...packages.values()].sort((a,b) => a.ecosystem.localeCompare(b.ecosystem) || a.name.localeCompare(b.name));
const licenses = {}; for (const item of items) licenses[item.license] = (licenses[item.license] || 0) + 1;
await mkdir(".artifacts", { recursive: true }); await writeFile(".artifacts/phase6-dependency-inventory.json", JSON.stringify({ generatedAtUtc: new Date().toISOString(), count: items.length, licenses, items }, null, 2));
console.log(JSON.stringify({ count: items.length, licenses }));
