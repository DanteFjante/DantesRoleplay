import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

import { completeReleaseEvidence } from "./complete-workload.mjs";

function optionsFrom(argv) {
  const options = { profiles: [], output: null };
  for (let index = 0; index < argv.length; index += 2) {
    const name = argv[index];
    const value = argv[index + 1];
    assert.ok(value, `Missing value for ${name}`);
    if (name === "--profile") options.profiles.push(resolve(value));
    else if (name === "--output") options.output = resolve(value);
    else throw new Error(`Unknown option ${name}`);
  }
  assert.ok(options.profiles.length > 0, "At least one --profile browser report is required");
  return options;
}

export async function verifyCompleteWorkload(profilePaths) {
  assert.ok(Array.isArray(profilePaths) && profilePaths.length > 0);
  const profiles = await Promise.all(profilePaths.map(async path => JSON.parse(await readFile(path, "utf8"))));
  const result = completeReleaseEvidence(profiles);
  return { schema: "dnd2024.complete-workload-verification.v1", verifiedAtUtc: new Date().toISOString(),
    profileCount: profiles.length, profilePaths: profilePaths.map(path => resolve(path)), result };
}

if (resolve(process.argv[1] ?? "") === fileURLToPath(import.meta.url)) {
  const options = optionsFrom(process.argv.slice(2));
  const report = await verifyCompleteWorkload(options.profiles);
  if (options.output) await writeFile(options.output, JSON.stringify(report, null, 2) + "\n", "utf8");
  process.stdout.write(JSON.stringify(report, null, 2) + "\n");
  if (report.result.status !== "passed") process.exitCode = 1;
}
