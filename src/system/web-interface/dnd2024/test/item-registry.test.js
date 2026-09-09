import assert from "node:assert/strict";
import test from "node:test";

import { ItemRegistryClient, readItemDefinition, readItemRegistry } from "../src/server/item-registry.ts";
import { ViewReadError } from "../src/data/view-read-client.ts";

const fingerprint = "A".repeat(64);
const contentFingerprint = "B".repeat(64);
const registryRecord = {
  collection: "dnd2024",
  kind: "entity",
  qualifiedId: "dnd2024.item.lantern.v1",
  name: "Lantern (D&D 2024, definition v1)",
  description: "Lantern definition.",
  path: "entities/adventuring-gear",
  status: "active",
  version: 1,
  contentFingerprint,
  sourceId: "dnd2024-catalog",
  sourceLogicalPath: "content/entities/items/lantern.json",
};

function json(payload, status = 200) {
  return new Response(JSON.stringify(payload), { status, headers: { "Content-Type": "application/json" } });
}

function page(records = [registryRecord], nextCursor = null, totalCount = records.length) {
  return {
    applicationId: "dnd2024",
    resolutionFingerprint: fingerprint,
    activeExtensions: [],
    resolvedWinners: records.map((record) => ({ record, ownerId: "base", sourceLabel: "D&D 2024 core",
      classification: "core", presentationRoles: ["entity", "adventuring-gear"], isAdditive: false })),
    additiveExtensionContent: [],
    availableKinds: ["entity"], totalCount, nextCursor,
  };
}

test("item registry uses a bounded server-side definition filter and source-bound continuation", async () => {
  const calls = [];
  const result = await readItemRegistry({
    serverOrigin: "https://table.test", applicationId: "dnd2024",
    request: { query: "  lantern  ", cursor: "page-two", expectedResolutionFingerprint: fingerprint },
    fetchImpl: async (url) => { calls.push(String(url)); return json(page()); },
  });
  const url = new URL(calls[0]);
  assert.equal(url.pathname, "/api/applications/dnd2024/content");
  assert.deepEqual(url.searchParams.getAll("kind"), ["entity"]);
  assert.deepEqual(url.searchParams.getAll("componentAny"), ["dnd2024.item-definition"]);
  assert.deepEqual(url.searchParams.getAll("archetype"), [
    "dnd2024.archetype.item-definition", "dnd2024.archetype.weapon-definition",
    "dnd2024.archetype.tool-definition", "dnd2024.archetype.consumable-definition",
  ]);
  assert.equal(url.searchParams.get("limit"), "40");
  assert.equal(url.searchParams.get("query"), "lantern");
  assert.equal(url.searchParams.get("cursor"), "page-two");
  assert.equal(result.records.length, 1);
  assert.equal(result.records[0].id, registryRecord.qualifiedId);
});

test("item registry caches equivalent pages and rejects a changed continuation", async () => {
  let requests = 0;
  const client = new ItemRegistryClient({ serverOrigin: "https://table.test", fetchImpl: async () => {
    requests += 1; return json(page());
  } });
  const request = { query: "", cursor: null, expectedResolutionFingerprint: null };
  await client.loadPage(request);
  await client.loadPage(request);
  assert.equal(requests, 1);
  assert.equal(client.metrics().hits, 1);
  await assert.rejects(readItemRegistry({
    serverOrigin: "https://table.test", applicationId: "dnd2024",
    request: { query: "", cursor: "next", expectedResolutionFingerprint: "C".repeat(64) },
    fetchImpl: async () => json(page()),
  }), (error) => error instanceof ViewReadError && error.category === "stale-data");
});

test("definition detail is definition-only, handles renamed and retired records, and marks incomplete data", async () => {
  const content = JSON.stringify({
    id: "item.renamed-lantern.v2",
    name: "Renamed Lantern",
    components: {
      "dnd2024.core.version": { revision: 2, status: "archived" },
      "dnd2024.item-definition": {
        definitionVersion: 2,
        kind: "adventuring-gear",
        stackPolicy: "separate",
        sourceRef: { sourceId: "fixture", locator: "Fixture > Renamed lantern" },
      },
    },
  });
  const result = await readItemDefinition({
    serverOrigin: "https://table.test", applicationId: "dnd2024",
    request: { id: registryRecord.qualifiedId, collection: "dnd2024",
      expectedContentFingerprint: contentFingerprint, sourceLabel: "Fixture source" },
    fetchImpl: async () => json({ summary: registryRecord, contentJson: content }),
  });
  assert.equal(result.details.name, "Renamed Lantern");
  assert.equal(result.details.quantity, null);
  assert.equal(result.details.container, null);
  assert.equal(result.details.state, "partial");
  assert.deepEqual(result.details.reasons, ["source-incomplete"]);
  assert.equal(result.details.properties.find((entry) => entry.label === "Record status").value, "Archived");
  assert.equal(result.details.properties.find((entry) => entry.label === "Definition version").value, 2);
});

test("definition detail rejects a record changed after its registry page", async () => {
  await assert.rejects(readItemDefinition({
    serverOrigin: "https://table.test", applicationId: "dnd2024",
    request: { id: registryRecord.qualifiedId, collection: "dnd2024",
      expectedContentFingerprint: "C".repeat(64), sourceLabel: null },
    fetchImpl: async () => json({ summary: registryRecord, contentJson: JSON.stringify({
      id: "item.lantern", name: "Lantern", components: { "dnd2024.item-definition": {
        definitionVersion: 1, kind: "adventuring-gear", massPounds: { numerator: 2, denominator: 1 },
      } },
    }) }),
  }), (error) => error instanceof ViewReadError && error.category === "stale-data");
});
