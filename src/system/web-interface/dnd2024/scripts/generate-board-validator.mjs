import { readFile, writeFile } from "node:fs/promises";
import Ajv2020 from "ajv/dist/2020.js";
import standalone from "ajv/dist/standalone/index.js";
const draft = process.argv.includes('--draft');
const query = JSON.parse(await readFile(new URL(`../../../../../catalog/applications/dnd2024/queries/combat/dnd2024.query.encounter-board${draft ? '-draft' : ''}.json`, import.meta.url), 'utf8'));

// Precompile the catalog schema so published pages never need eval/new Function or Ajv's compiler.
const ajv = new Ajv2020({ strict: true, code: { source: true, esm: true } });
const generated = standalone(ajv, ajv.compile(query.outputSchema))
  .replace('require("ajv/dist/runtime/ucs2length").default', 'ucs2length.default ?? ucs2length');
if (generated.includes("require(")) throw new Error("Unexpected validator runtime dependency; review before publishing.");
const output = '// Generated from the catalog query by scripts/generate-board-validator.mjs. Do not edit.\n' +
  'import ucs2length from "ajv/dist/runtime/ucs2length.js";\n' + generated + '\n';
const target = new URL(`../src/server/encounter-board${draft ? '-draft' : ''}-validator.js`, import.meta.url);
if (process.argv.includes('--check')) {
  if ((await readFile(target, 'utf8')).replace(/\r\n/g, '\n') !== output) throw new Error('Board validator drift: run node scripts/generate-board-validator.mjs.');
} else await writeFile(target, output);
