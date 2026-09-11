import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import ts from "typescript";

const sourceRoot = fileURLToPath(new URL("../src/", import.meta.url));
const equalityKinds = new Set([
  ts.SyntaxKind.EqualsEqualsToken, ts.SyntaxKind.EqualsEqualsEqualsToken,
  ts.SyntaxKind.ExclamationEqualsToken, ts.SyntaxKind.ExclamationEqualsEqualsToken,
]);
const compact = (node, file) => node.getText(file).replace(/\s+/gu, "");

function enclosingFunction(node) {
  for (let owner = node.parent; owner; owner = owner.parent) {
    if (ts.isFunctionDeclaration(owner)) return owner.name?.text ?? "";
  }
  return "";
}

function containsSchemaIdentity(node) {
  if (ts.isIdentifier(node) && /^(?:outputSchemaHash|schemaHash)$/u.test(node.text)) return true;
  if (ts.isStringLiteral(node) && /^(?:outputSchemaHash|schemaHash)$/u.test(node.text)) return true;
  // Passing metadata into a validator does not make its return value a schema
  // identity. Inspect hash operands, not arbitrary objects/closures/call bodies.
  if (ts.isCallExpression(node) || ts.isObjectLiteralExpression(node) ||
      ts.isArrowFunction(node) || ts.isFunctionExpression(node)) return false;
  return Boolean(ts.forEachChild(node, containsSchemaIdentity));
}

// These are individual reviewed uses, not file exclusions. The board draft is a
// computed command prerequisite that is subsequently proposed and explicitly
// accepted; it is not the current-board display reader. Item clients import only
// descriptor evidence from retained generated modules, never their validators.
const descriptorImports = new Map([
  ["server/item-view-client.ts", "./item-details-validator.js"],
  ["server/item-uses-client.ts", "./item-uses-validator.js"],
  ["server/item-recipes-client.ts", "./item-recipes-validator.js"],
]);

function inspectContractUses(name, source) {
  const file = ts.createSourceFile(name, source, ts.ScriptTarget.Latest, true,
    name.endsWith(".tsx") ? ts.ScriptKind.TSX : name.endsWith(".ts") ? ts.ScriptKind.TS : ts.ScriptKind.JS);
  const failures = [];
  let commandValidator = null;
  const note = (node, message) => failures.push(`${name}:${file.getLineAndCharacterOfPosition(node.getStart(file)).line + 1} ${message}`);
  function visit(node) {
    if (name.startsWith("server/") && ts.isNewExpression(node) &&
        ts.isIdentifier(node.expression) && node.expression.text === "URLSearchParams") {
      const fields = node.arguments?.[0];
      if (fields && ts.isObjectLiteralExpression(fields) && fields.properties.some((property) =>
        property.name && ["campaign", "campaignId"].includes(property.name.getText(file).replace(/["']/gu, ""))))
        note(node, "Retired application-aware API parameter; bind the route entity or a declared input instead.");
    }
    if (name.startsWith("server/") && ts.isCallExpression(node) && ts.isPropertyAccessExpression(node.expression) &&
        ["set", "append"].includes(node.expression.name.text) && node.arguments[0] &&
        ts.isStringLiteral(node.arguments[0]) && ["campaign", "campaignId"].includes(node.arguments[0].text))
      note(node, "Retired application-aware API parameter cannot be added to a request.");
    if (ts.isBinaryExpression(node) && equalityKinds.has(node.operatorToken.kind) &&
        (containsSchemaIdentity(node.left) || containsSchemaIdentity(node.right))) {
      const allowed = name === "server-host/main.tsx" && enclosingFunction(node) === "sameCampaignProjection" &&
        compact(node, file) === "previous.outputSchemaHash===current.outputSchemaHash";
      // This compares two server observations when merging deferred reads. It is
      // snapshot consistency, not equality to the browser's catalog/schema pin.
      if (!allowed) note(node, "Unreviewed schema-identity equality on a browser path.");
    }
    if (ts.isImportDeclaration(node) && ts.isStringLiteral(node.moduleSpecifier) &&
        /-validator\.js$/u.test(node.moduleSpecifier.text)) {
      const specifier = node.moduleSpecifier.text;
      const clause = node.importClause;
      const bindings = clause?.namedBindings;
      const metadataOnly = descriptorImports.get(name) === specifier && !clause?.name &&
        bindings && ts.isNamedImports(bindings) && bindings.elements.length === 1 &&
        (bindings.elements[0].propertyName?.text ?? bindings.elements[0].name.text) === "contract";
      const commandOnly = name === "server/board-draft.ts" &&
        specifier === "./encounter-board-draft-validator.js" && clause?.name?.text === "validate" && !bindings;
      if (commandOnly) commandValidator = clause.name;
      else if (!metadataOnly) note(node, "Generated whole-value validator imported by an unreviewed browser consumer.");
    }
    if (ts.isCallExpression(node) && node.arguments.some((argument) => ts.isStringLiteral(argument) &&
        /-validator\.js$/u.test(argument.text))) {
      note(node, "Dynamic generated-validator loading needs explicit boundary review.");
    }
    ts.forEachChild(node, visit);
  }
  visit(file);
  if (commandValidator) {
    function inspectCommandUse(node) {
      if (ts.isIdentifier(node) && node.text === commandValidator.text && node !== commandValidator) {
        const isPropertyLabel = ts.isPropertyAssignment(node.parent) && node.parent.name === node;
        const isCommandCall = ts.isCallExpression(node.parent) && node.parent.expression === node &&
          ["validateDraftProjection", "generateBoardDraft"].includes(enclosingFunction(node));
        if (!isPropertyLabel && !isCommandCall)
          note(node, "The command validator cannot be reused as a display gate or escape its reviewed calls.");
      }
      ts.forEachChild(node, inspectCommandUse);
    }
    inspectCommandUse(file);
  }
  return failures;
}

async function sourceFiles(directory) {
  const files = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const absolute = path.join(directory, entry.name);
    if (entry.isDirectory()) files.push(...await sourceFiles(absolute));
    else if (/\.(?:js|ts|tsx)$/u.test(entry.name) && !entry.name.endsWith(".d.ts")) files.push(absolute);
  }
  return files;
}

test("browser paths cannot reintroduce schema-hash admission or generated display validators", async () => {
  const failures = [];
  for (const absolute of await sourceFiles(sourceRoot)) {
    const relative = path.relative(sourceRoot, absolute).replaceAll("\\", "/");
    failures.push(...inspectContractUses(relative, await readFile(absolute, "utf8")));
  }
  assert.deepEqual(failures, []);
});

test("contract regression guard rejects likely relapses while preserving narrow command/evidence uses", () => {
  for (const source of [
    "const params = new URLSearchParams({perspective, campaignId, limit: '20'});",
    "url.searchParams.set('campaignId', value);",
    "if (response.outputSchemaHash !== contract.outputSchemaHash) return null;",
    "if (query['outputSchemaHash'] === expected) return data;",
    "const {outputSchemaHash} = value; if (outputSchemaHash !== expected) throw Error();",
    "import validate from './item-details-validator.js'; validate(value);",
    "import * as values from './item-details-validator.js'; values.default(value);",
    "const values = await import('./item-details-validator.js');",
  ]) assert.ok(inspectContractUses("server/item-view-client.ts", source).length, source);
  assert.deepEqual(inspectContractUses("server/item-view-client.ts",
    "import { contract } from './item-details-validator.js'; const evidence = value.outputSchemaHash;"), []);
  assert.deepEqual(inspectContractUses("server-host/main.tsx",
    "function sameCampaignProjection() { return previous.outputSchemaHash === current.outputSchemaHash; }"), []);
  assert.ok(inspectContractUses("server-host/main.tsx",
    "function unrelatedDisplay() { return previous.outputSchemaHash === current.outputSchemaHash; }").length);
  assert.ok(inspectContractUses("server/board-draft.ts",
    "import validate from './encounter-board-draft-validator.js'; function display(data) { return validate(data); }").length);
  assert.deepEqual(inspectContractUses("server/board-draft.ts",
    "import validate from './encounter-board-draft-validator.js'; function generateBoardDraft(data) { return validate(data); }"), []);
});
