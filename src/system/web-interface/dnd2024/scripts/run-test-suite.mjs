import assert from "node:assert/strict";
import { readdirSync } from "node:fs";
import { resolve } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("..", import.meta.url));
const mode = process.argv[2];
assert.ok(["node", "mounted", "shared"].includes(mode), "Expected the node, mounted or shared browser test suite");

const directoryName = mode === "node" ? "test" : mode === "shared" ? "../tests/browser" : "test/mounted";
const directory = resolve(root, directoryName);
const suffix = mode === "node" ? ".test.js" : mode === "shared" ? ".test.mjs" : ".test.tsx";
const files = readdirSync(directory, { withFileTypes: true })
  .filter(entry => entry.isFile() && entry.name.endsWith(suffix))
  .map(entry => `${directoryName}/${entry.name}`)
  .sort();
assert.ok(files.length > 0, `No ${mode} tests were found`);

const imports = mode === "mounted"
  ? ["--import", "./test/support/register-css-module-loader.mjs", "--import", "tsx"]
  : [];
const result = spawnSync(process.execPath, [...imports, "--test", ...files], { cwd: root, stdio: "inherit" });
if (result.error) throw result.error;
process.exitCode = result.status ?? 1;
