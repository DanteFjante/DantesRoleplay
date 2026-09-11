import { readFile, writeFile } from "node:fs/promises";
import { createHash } from "node:crypto";
import Ajv2020 from "ajv/dist/2020.js";
import standalone from "ajv/dist/standalone/index.js";

const targets = [
  {
    name: "item-details",
    source: "data/dnd2024.query.inventory-item-details.json",
    output: "item-details-validator.js",
    label: "Details",
    contractOnly: true,
    exportsContract: true,
  },
  {
    name: "item-recipes",
    source: "data/dnd2024.query.inventory-item-recipes.json",
    output: "item-recipes-validator.js",
    label: "Recipes",
    contractOnly: true,
    exportsContract: true,
  },
  {
    name: "item-uses",
    source: "data/dnd2024.query.inventory-item-uses.json",
    output: "item-uses-validator.js",
    label: "Uses",
    contractOnly: true,
    exportsContract: true,
  },
  {
    name: "encounter-board-draft",
    source: "combat/dnd2024.query.encounter-board-draft.json",
    output: "encounter-board-draft-validator.js",
    label: "encounter board draft",
  },
  {
    name: "campaign-summary-contract",
    source: "campaign/dnd2024.query.campaign-summary.json",
    output: "campaign-summary-contract.js",
    label: "Campaign summary",
    contractOnly: true,
  },
  {
    name: "campaign-context-contract",
    source: "campaign/dnd2024.query.campaign-context.json",
    output: "campaign-context-contract.js",
    label: "Campaign context",
    contractOnly: true,
  },
  {
    name: "campaign-details-contract",
    source: "campaign/dnd2024.query.campaign-details.json",
    output: "campaign-details-contract.js",
    label: "Campaign details",
    contractOnly: true,
  },
  {
    name: "campaign-location-visits-contract",
    source: "campaign/dnd2024.query.campaign-location-visits.json",
    output: "campaign-location-visits-contract.js",
    label: "Campaign location visits",
    contractOnly: true,
  },
  {
    name: "world-campaign-directory-contract",
    source: "campaign/dnd2024.query.world-campaign-directory.json",
    output: "world-campaign-directory-contract.js",
    label: "World campaign directory",
    contractOnly: true,
  },
  {
    name: "character-sheet-contract",
    source: "character/dnd2024.query.character-sheet-v2.json",
    output: "character-sheet-contract.js",
    label: "character sheet v2",
    contractOnly: true,
  },
  {
    name: "inventory-container-contract",
    source: "character/dnd2024.query.inventory-container.json",
    output: "inventory-container-contract.js",
    label: "bounded inventory container",
    contractOnly: true,
  },
  {
    name: "inventory-wallet-contract",
    source: "character/dnd2024.query.inventory-wallet.json",
    output: "inventory-wallet-contract.js",
    label: "bounded inventory wallet",
    contractOnly: true,
  },
  {
    name: "character-dossier-contract",
    source: "character/dnd2024.query.character-dossier-v1.json",
    output: "character-dossier-contract.js",
    label: "character dossier",
    contractOnly: true,
  },
  {
    name: "faction-directory-contract",
    source: "world/dnd2024.query.faction-directory-page.json",
    output: "faction-directory-contract.js",
    label: "faction directory page",
    contractOnly: true,
  },
  {
    name: "world-location-scope-page-contract",
    source: "world/dnd2024.query.world-location-scope-page.json",
    output: "world-location-scope-page-contract.js",
    label: "world location scope page",
    contractOnly: true,
  },
  {
    name: "world-people-holdings-page-contract",
    source: "world/dnd2024.query.world-people-holdings-page.json",
    output: "world-people-holdings-page-contract.js",
    label: "world people and holdings page",
    contractOnly: true,
  },
];

function usesVersionTwo(value) {
  if (Array.isArray(value)) return value.some(usesVersionTwo);
  if (!value || typeof value !== "object") return false;
  return Object.entries(value).some(([key, child]) =>
    key === "pattern" || key === "format" || usesVersionTwo(child));
}

function objectOutputSchemaHash(schema) {
  const profile = `system-json-schema-2020-12/${usesVersionTwo(schema) ? "v2" : "v1"}`;
  const fingerprintEnvelope = `{"profile":${JSON.stringify(profile)},"schema":${JSON.stringify(schema)}}`;
  return createHash("sha256").update(fingerprintEnvelope).digest("hex").toUpperCase();
}

const check = process.argv.includes("--check");
const unknownArguments = process.argv.slice(2).filter((argument) => argument !== "--check");
if (unknownArguments.length > 0) {
  throw new Error(`Unknown validator-generator argument: ${unknownArguments.join(", ")}`);
}

for (const target of targets) {
  const queryUrl = new URL(`../../../../../catalog/applications/dnd2024/queries/${target.source}`, import.meta.url);
  const query = JSON.parse(await readFile(queryUrl, "utf8"));
  const fieldBased = query.executor === "object-projection" && query.profile === "application-object/v2";
  if (fieldBased && (!target.contractOnly || Object.hasOwn(query, "outputSchema"))) {
    throw new Error(`Field-based query ${query.id} must not generate a domain value validator.`);
  }
  // Kept as transport evidence only. Display readers do not admit values by comparing
  // this hash with a build-time domain schema.
  const outputSchema = fieldBased ? { type: "object" } : query.outputSchema;
  if (!outputSchema || typeof outputSchema !== "object" || Array.isArray(outputSchema)) {
    throw new Error(`Query ${query.id} has no recognized output contract.`);
  }
  const ajvOptions = {
    strict: true,
    ...(target.strictTypes === undefined ? {} : { strictTypes: target.strictTypes }),
    code: { source: true, esm: true },
  };
  const ajv = target.contractOnly ? null : new Ajv2020(ajvOptions);
  const generated = target.contractOnly ? "" : standalone(ajv, ajv.compile(query.outputSchema))
    .replaceAll('require("ajv/dist/runtime/ucs2length").default', "ucs2length.default ?? ucs2length");
  if (generated.includes("require(")) throw new Error(`Unexpected validator runtime dependency for ${target.name}; review before publishing.`);

  const header = `// Generated by scripts/generate-validators.mjs from the catalog ${target.label} query. Do not edit.\n`;
  const runtimeImport = target.contractOnly ? "" : 'import ucs2length from "ajv/dist/runtime/ucs2length.js";\n';
  const contractExport = target.contractOnly
    ? `export const contract = ${JSON.stringify(target.exportsContract
      ? { id: query.id, ...query.projection }
      : { id: query.id, outputSchemaHash: query.executor === "object-projection" ? objectOutputSchemaHash(outputSchema) : query.projection.outputSchemaHash })};\n`
    : target.exportsContract
      ? `export const contract = ${JSON.stringify({ id: query.id, ...query.projection })};\n`
    : "";
  const output = header + runtimeImport + contractExport + generated + (target.contractOnly ? "" : "\n");
  const outputUrl = new URL(`../src/server/${target.output}`, import.meta.url);

  if (check) {
    const current = (await readFile(outputUrl, "utf8")).replace(/\r\n/g, "\n");
    if (current !== output) {
      throw new Error(`Validator drift for ${target.name}: run node scripts/generate-validators.mjs.`);
    }
  } else {
    await writeFile(outputUrl, output);
  }
}
