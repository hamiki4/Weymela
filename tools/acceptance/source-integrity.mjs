// Compare source inputs only; never stage, commit, copy, delete, or inspect live environment files.
import { readFile, readdir, mkdir, writeFile } from "node:fs/promises";
import { resolve, join } from "node:path";
import { createHash } from "node:crypto";
const baseline = resolve(process.argv[2] || "/opt/WeymelaV3"); const candidate = resolve(process.cwd());
if (baseline !== "/opt/WeymelaV3" || !candidate.startsWith("/tmp/weymela-v3-phase6-")) throw new Error("Only the isolated V3 acceptance copy is supported");
const excluded = new Set([".git", "bin", "obj", "node_modules", "dist", ".artifacts", "artifacts", "test-results", "playwright-report"]);
async function files(root, relative = "") {
  const result=[];
  for(const entry of await readdir(join(root,relative),{withFileTypes:true})) {
    if(excluded.has(entry.name)||entry.name.startsWith(".env")&&entry.name!==".env.example")continue;
    const name=join(relative,entry.name);
    if(entry.isDirectory())result.push(...await files(root,name));
    else if(entry.isFile())result.push(name);
    else throw new Error(`Unexpected source symlink/special file: ${name}`);
  } return result.sort();
}
const previous=await files(baseline), current=await files(candidate); const changes=[], whitespace=[], inputs=[];
const sha=data=>createHash("sha256").update(data).digest("hex");
for(const name of current) {
  const bytes=await readFile(join(candidate,name)); const hash=sha(bytes); inputs.push({path:name,sha256:hash});
  const old=previous.includes(name)?sha(await readFile(join(baseline,name))):null;
  if(hash===old)continue;
  changes.push({path:name,previousSha256:old,sha256:hash});
  if(!bytes.includes(0)) {
    bytes.toString("utf8").split(/\r?\n/).forEach((line,index) => { if(/[ \t]+$/.test(line))whitespace.push({path:name,line:index+1}); });
  }
}
const omitted=previous.filter(name=>!current.includes(name));
await mkdir(".artifacts",{recursive:true});
const report={baseline,candidate,sourceInputCount:current.length,sourceInputSha256:sha(JSON.stringify(inputs)),changes,omitted,whitespace,inputs};
await writeFile(".artifacts/phase6-source-integrity.json",JSON.stringify(report,null,2));
console.log(JSON.stringify({sourceInputCount:current.length,changedFiles:changes.length,omitted,whitespace}));
if(omitted.length||whitespace.length)process.exitCode=1;
