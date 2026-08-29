#!/usr/bin/env node
/**
 * Slice 2 gate for the Thalorien map page.
 *
 * A published page cannot enforce an audience: everything embedded in it is shipped, and hiding a
 * record with JavaScript is not secrecy. So the page carries public world information only, and
 * this check proves that against the live world before the page is published.
 *
 * Usage:  node verify-audience.mjs [--host http://localhost:6217] [--drafts=fail|warn]
 * Exit 0 only when every embedded place is present, public, and not archived.
 */
import { readFile } from "node:fs/promises";

const args = process.argv.slice(2);
const host = (args.find((a) => a.startsWith("--host")) ?? "--host=http://localhost:6217").split("=")[1]
  ?? "http://localhost:6217";
const draftMode = (args.find((a) => a.startsWith("--drafts")) ?? "--drafts=warn").split("=")[1];

const html = await readFile(new URL("./index.html", import.meta.url), "utf8");
const match = html.match(/const MAP = (\{[\s\S]*?\});\n/);
if (!match) {
  console.error("FAIL: could not find the embedded MAP table in index.html");
  process.exit(2);
}
const map = JSON.parse(match[1]);
const ids = Object.keys(map.places);

const problems = [];
const drafts = [];
let checked = 0;

for (const id of ids) {
  let entity = null;
  try {
    const response = await fetch(`${host}/api/data/entity/${encodeURIComponent(id)}`);
    entity = response.ok ? await response.json() : null;
  } catch (error) {
    console.error(`FAIL: could not reach ${host} (${error.message})`);
    process.exit(2);
  }
  if (!entity) { problems.push(`${id}: not present in the live world`); continue; }

  const component = (entity.components ?? []).find((c) => c.definitionId === "game.core.world.location");
  if (!component) { problems.push(`${id}: has no game.core.world.location component`); continue; }

  let data = component.data;
  if (typeof data === "string") { try { data = JSON.parse(data); } catch { data = null; } }
  if (!data) { problems.push(`${id}: location component data is unreadable`); continue; }

  checked += 1;
  if (data.visibility !== "public") problems.push(`${id}: visibility is "${data.visibility}", not public`);
  if (data.status === "archived") problems.push(`${id}: status is archived`);
  if (data.status === "draft") drafts.push(id);
}

console.log(`checked ${checked}/${ids.length} embedded places against ${host}`);

if (drafts.length) {
  const line = `${drafts.length} embedded place(s) are still status "draft": ${drafts.join(", ")}`;
  if (draftMode === "fail") problems.push(line);
  else console.warn(`WARN: ${line}`);
}

// The page must never carry a secret record, whatever its visibility.
for (const marker of ["secret.thalorien.", "dmSecret", "dmTruth"]) {
  if (html.includes(marker)) problems.push(`page contains "${marker}"`);
}

if (problems.length) {
  console.error("\nFAIL");
  for (const problem of problems) console.error(" - " + problem);
  process.exit(1);
}
console.log("PASS: every embedded place is public and not archived, and no secret record is present.");
