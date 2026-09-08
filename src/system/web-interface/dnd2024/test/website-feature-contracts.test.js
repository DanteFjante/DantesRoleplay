import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { CAMPAIGN_SECTIONS, LOCATION_SECTIONS, MAIN_TABS, WORLD_SECTIONS } from "../src/state.js";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../../..");
const contractPath = path.join(root, "src/system/web-interface/dnd2024/contracts/website-feature-contracts.json");
const catalogQueryRoot = path.join(root, "catalog/applications/dnd2024/queries");
const catalogObjectRoot = path.join(root, "catalog/applications/dnd2024/objects");
const readJson = async (file) => JSON.parse(await readFile(file, "utf8"));
const contract = await readJson(contractPath);

const expectedFeatures = [
  "context-and-shell", "campaign", "party-roster", "character", "inventory-and-items",
  "world-overview", "maps-locations-and-routes", "people-and-holdings", "factions",
  "lore-and-history", "current-and-play", "tactical-board", "rules", "installed-content",
];
const expectedProfiles = ["game-master", "gm-player-preview", "actor", "unbound"];
const positiveInteger = (value) => Number.isSafeInteger(value) && value > 0;

async function findCatalogRecord(directory, id) {
  const { readdir } = await import("node:fs/promises");
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const candidate = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      const nested = await findCatalogRecord(candidate, id);
      if (nested) return nested;
    } else if (entry.name.endsWith(".json")) {
      const value = await readJson(candidate);
      if (value.id === id) return value;
    }
  }
  return null;
}

test("W01 freezes one explicit contract for every visible website surface", () => {
  assert.equal(contract.schema, "dnd2024.website-feature-contracts.v1");
  assert.equal(contract.slice, "W01");
  assert.deepEqual(contract.features.map(({ id }) => id), expectedFeatures);
  assert.equal(new Set(contract.features.map(({ id }) => id)).size, expectedFeatures.length);
  assert.deepEqual(contract.decisions.newPermanentIdsRegisteredByW01, []);
  assert.equal(contract.decisions.persistDistinctPartyEntity, false);
  assert.match(contract.decisions.partyOwner, /has-character-participation/u);
  assert.match(contract.authority.forbiddenShortcut, /must not become an authoritative game-state store/u);

  for (const feature of contract.features) {
    assert.ok(Array.isArray(feature.screens) && feature.screens.length > 0, `${feature.id} screens`);
    assert.ok(Array.isArray(feature.currentOwner) && feature.currentOwner.length > 0, `${feature.id} owner`);
    for (const field of ["browserAssembly", "authority", "editCapability", "completeness", "currentCost"])
      assert.ok(typeof feature[field] === "string" && feature[field].length > 20, `${feature.id} ${field}`);
    assert.deepEqual(Object.keys(feature.audience), expectedProfiles, `${feature.id} audience profiles`);
    assert.deepEqual(feature.fixtureCases, ["small", "large", "unauthorized"], `${feature.id} fixtures`);
    for (const field of ["requests", "sqlStatements", "responseBytes", "retainedResponseBytes"])
      assert.ok(positiveInteger(feature.gates[field]), `${feature.id} ${field}`);
    assert.ok(feature.gates.retainedResponseBytes >= feature.gates.responseBytes, `${feature.id} memory gate`);
    for (const field of ["pageSize", "maximumPages", "maximumRecords"])
      assert.ok(positiveInteger(feature.target[field]), `${feature.id} target ${field}`);
    assert.ok(feature.target.pageSize * feature.target.maximumPages >= feature.target.maximumRecords,
      `${feature.id} pagination covers its maximum records`);
  }
});

test("W01 preserves all existing registered query and object owners at their current versions", async () => {
  const expectedQueries = [
    "dnd2024.query.campaign-summary",
    "dnd2024.query.character-dossier-v1",
    "dnd2024.query.character-sheet-v2",
    "dnd2024.query.inventory-container",
    "dnd2024.query.inventory-item-details",
    "dnd2024.query.inventory-item-uses",
    "dnd2024.query.inventory-item-recipes",
    "dnd2024.query.faction-directory-page",
    "dnd2024.query.current-scene",
    "dnd2024.query.encounter-board",
    "dnd2024.query.encounter-board-draft",
  ];
  for (const id of expectedQueries) {
    const query = await findCatalogRecord(catalogQueryRoot, id);
    assert.equal(query?.status, "active", `${id} remains active`);
  }

  const versions = new Map([
    ["dnd2024.object.campaign-summary", 3],
    ["dnd2024.object.character-dossier-records", 1],
    ["dnd2024.object.faction-directory-page", 1],
  ]);
  for (const [id, version] of versions) {
    const object = await findCatalogRecord(catalogObjectRoot, id);
    assert.equal(object?.version, version, `${id} version`);
  }
});

test("W06 registers the bounded World/location scope owner", async () => {
  const query = await findCatalogRecord(catalogQueryRoot, "dnd2024.query.world-location-scope");
  assert.equal(query?.status, "active");
  assert.equal(query?.executor, "mechanic-projection");
  assert.equal(query?.exposure, "binding-only");
  assert.deepEqual(query?.roles, { scope: "The exact World root or previously authorized location scope." });
  assert.equal(query?.outputSchema?.properties?.limits?.properties?.contentsDepth?.const, 1);
  assert.equal(query?.outputSchema?.properties?.locations?.maxItems, 100);
});

test("W01 coverage follows every current navigation section and item/board route", () => {
  const coverage = contract.navigationCoverage;
  assert.deepEqual(coverage.persistentShell, ["context-and-shell"]);
  assert.deepEqual(Object.keys(coverage.mainTabs), MAIN_TABS.map(({ id }) => id));
  assert.deepEqual(Object.keys(coverage.campaignSections), CAMPAIGN_SECTIONS.map(({ id }) => id));
  assert.deepEqual(Object.keys(coverage.worldSections), WORLD_SECTIONS.map(({ id }) => id));
  assert.deepEqual(Object.keys(coverage.locationSections), LOCATION_SECTIONS.map(({ id }) => id));
  assert.deepEqual(Object.keys(coverage.itemRoutes), ["inventory", "details", "uses", "recipes"]);
  assert.deepEqual(Object.keys(coverage.boardActions),
    ["generate", "upload-optional-background", "prepare", "confirm", "execute"]);
  const featureIds = new Set(expectedFeatures);
  const covered = [
    ...coverage.persistentShell,
    ...Object.values(coverage.mainTabs).flat(),
    ...Object.values(coverage.campaignSections),
    ...Object.values(coverage.worldSections),
    ...Object.values(coverage.locationSections),
    ...Object.values(coverage.itemRoutes),
    ...Object.values(coverage.boardActions),
  ];
  assert.ok(covered.every((id) => featureIds.has(id)));
  assert.ok(expectedFeatures.every((id) => covered.includes(id)), "every feature has a visible route");
});

test("W01 fixture profiles and whole-workload gates are bounded and fail closed", () => {
  assert.deepEqual(Object.keys(contract.fixtures), ["small", "large", "unauthorized"]);
  assert.ok(contract.fixtures.large.locations > contract.fixtures.small.locations);
  assert.ok(contract.fixtures.large.installedRecords > contract.fixtures.small.installedRecords);
  assert.deepEqual(contract.fixtures.unauthorized.profiles,
    ["unbound", "wrong-campaign-actor", "gm-player-preview"]);
  assert.ok(contract.fixtures.unauthorized.canaries.length >= 4);

  const gates = contract.completeWorkloadGates;
  assert.deepEqual(gates.authorizedProfiles, ["game-master", "gm-player-preview", "actor"]);
  assert.deepEqual([...gates.featureInteractions, ...gates.independentRouteInteractions], expectedFeatures);
  for (const field of ["maximumDataRequests", "maximumSqlStatements", "maximumResponseBytes",
    "maximumRetainedResponseBytes", "minimumColdSamples", "minimumWarmSamples"])
    assert.ok(positiveInteger(gates[field]), field);
  assert.equal(gates.warmSourceReads, 0);
  assert.equal(gates.maximumDataRequests, 200);
  assert.equal(gates.maximumSqlStatements, 80);
  assert.equal(gates.minimumColdSamples, 20);
  assert.equal(gates.minimumWarmSamples, 20);
  const features = new Map(contract.features.map((feature) => [feature.id, feature]));
  const core = gates.featureInteractions.map((id) => features.get(id));
  assert.ok(core.reduce((total, feature) => total + feature.gates.requests, 0) <= gates.maximumDataRequests);
  assert.ok(core.reduce((total, feature) => total + feature.gates.responseBytes, 0) <= gates.maximumResponseBytes);
  for (const feature of contract.features)
    assert.ok(feature.gates.requests * feature.target.maximumPages <= gates.maximumDataRequests,
      `${feature.id} complete continuation traversal is bounded`);
});

test("W01 compatibility sequence keeps data, server, and website versions deployable", () => {
  const order = contract.versioning.compatibilityOrder;
  assert.equal(order.length, 6);
  assert.match(order[0], /add and validate/u);
  assert.match(order[1], /read both old and new/u);
  assert.match(order[2], /migrate or backfill/u);
  assert.match(order[3], /activate/u);
  assert.match(order[4], /publish/u);
  assert.match(order[5], /remove the old reader/u);
  assert.deepEqual(Object.keys(contract.versioning.migrationRules),
    ["queryOnlyProjection", "componentShapeChange", "relationshipMeaningChange", "uiAssemblyOnly"]);
});
