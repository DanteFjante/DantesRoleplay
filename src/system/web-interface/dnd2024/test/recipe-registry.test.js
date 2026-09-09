import assert from "node:assert/strict";
import test from "node:test";

import { RecipeRegistryClient, readRecipeDefinition, readRecipeRegistry } from "../src/server/recipe-registry.ts";
import { ViewReadError } from "../src/data/view-read-client.ts";

const resolutionFingerprint = "A".repeat(64);
const recipeFingerprint = "B".repeat(64);
const itemFingerprint = "C".repeat(64);
const recipeRecord = {
  collection: "dnd2024", kind: "entity", qualifiedId: "dnd2024.recipe.elixir",
  name: "Brew Elixir", description: "A recipe.", path: "entities/recipes", status: "active", version: 1,
  contentFingerprint: recipeFingerprint, sourceId: "dnd2024-catalog",
  sourceLogicalPath: "content/entities/recipes/elixir.json",
};
const itemRecord = {
  ...recipeRecord, qualifiedId: "dnd2024.item.elixir", name: "Elixir", contentFingerprint: itemFingerprint,
  sourceLogicalPath: "content/entities/items/elixir.json",
};

function json(payload, status = 200) {
  return new Response(JSON.stringify(payload), { status, headers: { "Content-Type": "application/json" } });
}

function page(record = recipeRecord, nextCursor = null, totalCount = record ? 1 : 0) {
  return { applicationId: "dnd2024", resolutionFingerprint, activeExtensions: [],
    resolvedWinners: record ? [{ record, ownerId: "base", sourceLabel: "D&D 2024 core",
      classification: "core", presentationRoles: ["entity"], isAdditive: false }] : [],
    additiveExtensionContent: [], availableKinds: ["entity"], totalCount, nextCursor };
}

const completeRecipe = JSON.stringify({
  id: recipeRecord.qualifiedId,
  name: recipeRecord.name,
  components: {
    "dnd2024.core.source": { citations: [{ locator: "Fixture rules > Brew Elixir" }] },
    "dnd2024.crafting.recipe": {
      outputs: [{ definition: { entityId: itemRecord.qualifiedId }, quantity: 2 }],
      materialRequirements: [{ definition: { entityId: "dnd2024.item.herb" }, quantity: 3 }],
      workDuration: { kind: "measured", amount: 4, unit: { entityId: "dnd2024.vocabulary.time-unit.hour" } },
      toolRequirement: { operator: "predicate", predicateId: "predicate.proficiency.tool",
        arguments: [{ entityId: "dnd2024.item.alchemist-supplies" }] },
      crafterRequirement: { operator: "predicate", predicateId: "predicate.level.minimum", arguments: [5] },
    },
  },
}, null, "\t") + "\n";

test("recipe registry uses recipe and reference indexes with source-bound paging", async () => {
  const calls = [];
  const result = await readRecipeRegistry({ serverOrigin: "https://table.test", applicationId: "dnd2024",
    request: { query: "  elixir  ", cursor: "next-page", expectedResolutionFingerprint: resolutionFingerprint,
      relatedItemId: itemRecord.qualifiedId },
    fetchImpl: async (url) => { calls.push(String(url)); return json(page()); } });
  const url = new URL(calls[0]);
  assert.deepEqual(url.searchParams.getAll("componentAny"), ["dnd2024.crafting.recipe"]);
  assert.deepEqual(url.searchParams.getAll("archetype"), ["dnd2024.archetype.crafting-recipe"]);
  assert.deepEqual(url.searchParams.getAll("referenceAny"), [itemRecord.qualifiedId]);
  assert.equal(url.searchParams.get("limit"), "12");
  assert.equal(url.searchParams.get("query"), "elixir");
  assert.equal(url.searchParams.get("cursor"), "next-page");
  assert.equal(result.records[0].id, recipeRecord.qualifiedId);

  await assert.rejects(readRecipeRegistry({ serverOrigin: "https://table.test", applicationId: "dnd2024",
    request: { query: "", cursor: "next", expectedResolutionFingerprint: "D".repeat(64), relatedItemId: null },
    fetchImpl: async () => json(page()) }),
  (error) => error instanceof ViewReadError && error.category === "stale-data");
});

test("recipe registry caches list and detail resources without inventory reads", async () => {
  let requests = 0;
  const client = new RecipeRegistryClient({ serverOrigin: "https://table.test", fetchImpl: async (input) => {
    requests += 1;
    const url = new URL(String(input));
    return url.pathname.includes("/catalog/records/")
      ? json({ summary: recipeRecord, contentJson: completeRecipe })
      : url.searchParams.has("id") ? json(page(itemRecord)) : json(page());
  } });
  const listRequest = { query: "", cursor: null, expectedResolutionFingerprint: null, relatedItemId: null };
  await client.loadPage(listRequest); await client.loadPage(listRequest);
  const detailRequest = { id: recipeRecord.qualifiedId, collection: "dnd2024",
    expectedContentFingerprint: recipeFingerprint, sourceLabel: "D&D 2024 core" };
  await client.loadDefinition(detailRequest); await client.loadDefinition(detailRequest);
  assert.equal(requests, 3);
  assert.equal(client.metrics().hits, 2);
});

test("recipe detail presents links, duration, knowledge, and unsupported requirements without evaluating them", async () => {
  const calls = [];
  const result = await readRecipeDefinition({ serverOrigin: "https://table.test", applicationId: "dnd2024",
    request: { id: recipeRecord.qualifiedId, collection: "dnd2024",
      expectedContentFingerprint: recipeFingerprint, sourceLabel: "Fixture source" },
    fetchImpl: async (input, init) => {
      calls.push({ url: String(input), method: init?.method ?? "GET" });
      return calls.length === 1 ? json({ summary: recipeRecord, contentJson: completeRecipe }) : json(page(itemRecord));
    } });
  assert.equal(result.entry.availability, "not-evaluated");
  assert.equal(result.entry.knowledgeState, "known");
  assert.equal(result.entry.duration, "4 Hour");
  assert.deepEqual(result.entry.outputs, [{ name: "Elixir", definitionId: itemRecord.qualifiedId, quantity: 2 }]);
  assert.match(result.entry.requirements.find((value) => value.label === "Crafter requirement").value,
    /Unsupported requirement/);
  assert.deepEqual(result.linkedItems.map((value) => value.id), [itemRecord.qualifiedId]);
  assert.deepEqual(calls.map((value) => value.method), ["GET", "GET"]);
  const linkRequest = new URL(calls[1].url);
  assert.deepEqual(linkRequest.searchParams.getAll("id"), [itemRecord.qualifiedId,
    "dnd2024.item.herb", "dnd2024.item.alchemist-supplies"]);
});

test("missing outputs and absent linked definitions remain explicit while changed records are rejected", async () => {
  const incomplete = JSON.stringify({ id: recipeRecord.qualifiedId, name: "Incomplete recipe", components: {
    "dnd2024.crafting.recipe": { outputs: [], workDuration: { kind: "special" },
      toolRequirement: { operator: "predicate", predicateId: "predicate.proficiency.tool", arguments: [] },
      crafterRequirement: { operator: "predicate", predicateId: "predicate.proficiency.crafter", arguments: [] } },
  } });
  const result = await readRecipeDefinition({ serverOrigin: "https://table.test", applicationId: "dnd2024",
    request: { id: recipeRecord.qualifiedId, collection: "dnd2024",
      expectedContentFingerprint: recipeFingerprint, sourceLabel: null },
    fetchImpl: async () => json({ summary: recipeRecord, contentJson: incomplete }) });
  assert.equal(result.entry.availability, "definition-incomplete");
  assert.deepEqual(result.entry.outputs, []);
  assert.deepEqual(result.linkedItems, []);

  let linkRead = 0;
  const definitionWithoutLinks = await readRecipeDefinition({ serverOrigin: "https://table.test", applicationId: "dnd2024",
    request: { id: recipeRecord.qualifiedId, collection: "dnd2024",
      expectedContentFingerprint: recipeFingerprint, sourceLabel: null },
    fetchImpl: async () => ++linkRead === 1
      ? json({ summary: recipeRecord, contentJson: completeRecipe })
      : json({ message: "supporting index unavailable" }, 503) });
  assert.equal(definitionWithoutLinks.entry.outputs[0].definitionId, itemRecord.qualifiedId);
  assert.deepEqual(definitionWithoutLinks.linkedItems, []);

  await assert.rejects(readRecipeDefinition({ serverOrigin: "https://table.test", applicationId: "dnd2024",
    request: { id: recipeRecord.qualifiedId, collection: "dnd2024",
      expectedContentFingerprint: "D".repeat(64), sourceLabel: null },
    fetchImpl: async () => json({ summary: recipeRecord, contentJson: incomplete }) }),
  (error) => error instanceof ViewReadError && error.category === "stale-data");
});
