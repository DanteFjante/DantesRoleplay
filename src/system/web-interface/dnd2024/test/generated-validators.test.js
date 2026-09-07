import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import test from "node:test";

test("all precompiled validators match their exact catalog contracts", () => {
  const generator = fileURLToPath(new URL("../scripts/generate-validators.mjs", import.meta.url));
  execFileSync(process.execPath, [generator, "--check"]);
});
