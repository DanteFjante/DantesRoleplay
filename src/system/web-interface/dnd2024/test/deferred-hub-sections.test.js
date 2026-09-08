import assert from "node:assert/strict";
import test from "node:test";
import { readDeferredHubSection } from "../src/server/game-server-context.js";
import { contract as worldCampaignDirectoryContract } from "../src/server/world-campaign-directory-contract.js";
import { contract as worldLocationScopeContract } from "../src/server/world-location-scope-contract.js";

const origin = "http://localhost:6217";
const source = {
  applicationId: "dnd2024", stateSpaceId: "state.fixture",
  campaign: { id: "campaign.caldris.fixture", name: "Fixture" },
  actor: { id: "actor.fixture" }, party: [],
  audience: { seat: "dm", perspective: "dm" },
  contextSelection: { selectedWorldId: "world.caldris", selectedCampaignId: "campaign.caldris.fixture" },
  knowledge: { status: "unavailable", entries: [], locations: [] },
};
const response = (value, status = 200) => new Response(JSON.stringify(value), { status });
const component = (id, type, value) => response({
  entityId: id, qualifiedTypeId: type, valueJson: JSON.stringify(value),
});

for (const perspective of ["dm", "player"]) {
  test(`deferred history reads all 251 authorized entries with one request (${perspective})`, async () => {
    const entries = Array.from({ length: 251 }, (_, i) => ({
      id: `history.${i}`, occurredAtMinute: i, dateLabel: `Day ${i}`,
      precision: "exact", title: `Event ${i}`, summary: "An event.",
      ...(perspective === "dm" ? { subjects: [] } : {}),
    }));
    const calls = [];
    const result = await readDeferredHubSection({
      origin, source: { ...source, audience: { seat: "dm", perspective } }, section: "history",
      fetchImpl: async (input) => {
        const target = new URL(input); calls.push(target);
        assert.equal(target.searchParams.get("perspective"), perspective);
        return response({ status: "ready", perspective, entries });
      },
    });
    assert.deepEqual(result.chronology.entries, entries);
    assert.equal(calls.length, 1);
    assert.ok(calls[0].pathname.endsWith("/chronology"));
  });
}

test("deferred empty, unavailable and malformed history cannot be confused", async () => {
  const load = (value, status = 200) => readDeferredHubSection({
    origin, source, section: "history", fetchImpl: async () => response(value, status),
  });
  assert.equal((await load({ status: "empty", perspective: "dm", entries: [] })).chronology.status, "empty");
  await assert.rejects(load({}, 500), /unavailable/);
  await assert.rejects(load({ status: "ready", perspective: "player", entries: [] }), /unavailable/);
});

test("deferred history preserves the 500-entry boundary and rejects overflow", async () => {
  const entries = Array.from({ length: 500 }, (_, i) => ({
    id: `history.${i}`, occurredAtMinute: i, dateLabel: `Day ${i}`,
    precision: "exact", title: `Event ${i}`, summary: "An event.", subjects: [],
  }));
  const load = (items) => readDeferredHubSection({
    origin, source, section: "history",
    fetchImpl: async () => response({ status: "ready", perspective: "dm", entries: items }),
  });
  assert.equal((await load(entries)).chronology.entries.length, 500);
  await assert.rejects(load([...entries, { ...entries[0], id: "history.overflow" }]), /unavailable/u);
});

test("preview lore and people never request ambient DM knowledge or media", async () => {
  for (const section of ["lore", "people"]) {
    const calls = [];
    await assert.rejects(readDeferredHubSection({
      origin, section, source: { ...source, locationDirectory: [], audience: { seat: "dm", perspective: "player" } },
      fetchImpl: async (input) => { calls.push(input); throw new Error("Private read"); },
    }), /Actor binding/);
    assert.deepEqual(calls, []);
  }
});

test("Actor lore uses the authorized notebook and preserves all entries", async () => {
  const entries = [{ text: "A familiar rumour.", stance: "familiar", presentationKind: "lore" }];
  const result = await readDeferredHubSection({
    origin, section: "lore", source: { ...source, audience: { seat: "player", perspective: "player" } },
    fetchImpl: async (input) => {
      assert.ok(new URL(input).pathname.endsWith("/knowledge"));
      return response({ status: "ready", entries, locations: [] });
    },
  });
  assert.deepEqual(result.knowledge.entries, entries);
});

test("deferred locations read one exact root scope and suppress ambient media in preview", async () => {
  const calls = [];
  const locations = Array.from({ length: 100 }, (_, index) => ({
    id: `place-${index}`, name: `Place ${index}`, parentId: "world.caldris", slot: "region",
    kind: "region", status: index % 2 ? "draft" : "active", summary: "A place.",
    visibility: "public", mapAnchor: null,
  }));
  const result = await readDeferredHubSection({
    origin, section: "locations", source: { ...source, audience: { seat: "dm", perspective: "player" } },
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target);
      return response({
        applicationId: "dnd2024", stateSpaceId: "state.fixture",
        qualifiedQueryId: worldLocationScopeContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: worldLocationScopeContract.outputSchemaHash,
        resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
        data: {
          version: 1, state: "ready",
          scope: { id: "world.caldris", name: "Caldris", parentId: null, slot: "", kind: "world",
            status: "active", summary: "A gentle world.", visibility: "public", mapAnchor: null },
          locations, limits: { contentsDepth: 1, locationCount: 100, complete: true },
        },
      });
    },
  });
  assert.equal(result.locationDirectory.length, 101);
  assert.equal(calls.length, 1);
  assert.match(calls[0].pathname, /entities\/world\.caldris\/read-models\/dnd2024\.query\.world-location-scope$/u);
  assert.ok(calls.every((target) => !target.pathname.endsWith("/entities") && !target.pathname.endsWith("/media")));
});

test("failed or incompatible root scopes never produce empty location success", async () => {
  for (const value of [response({}, 500), response({ unexpected: true })]) {
    await assert.rejects(readDeferredHubSection({
      origin, source, section: "locations", fetchImpl: async () => value,
    }), /scope/);
  }
});

test("Current reuses loaded authorized locations and resolves the existing current-scene contract", async () => {
  const calls = [];
  const result = await readDeferredHubSection({
    origin, section: "current",
    source: { ...source, locationDirectory: [{ id: "location.caldris.one", name: "Place" }] },
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target);
      if (target.pathname.endsWith("/components/game.core.campaign.current-scene"))
        return component(source.campaign.id, "game.core.campaign.current-scene",
          { location: { entityId: "location.caldris.one" } });
      return response({}, 404);
    },
  });
  assert.equal(result.currentSituation.status, "ready");
  assert.equal(result.currentSituation.locationId, "location.caldris.one");
  assert.ok(calls.every((target) => !target.pathname.endsWith("/entities")));
});

test("context discovery follows every registered continuation without hydrating entities", async () => {
  const calls = [];
  const result = await readDeferredHubSection({
    origin, section: "context", source,
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target);
      assert.match(target.pathname, /dnd2024\.query\.world-campaign-directory/u);
      const second = target.searchParams.has("cursor");
      const campaign = {
        id: second ? "campaign.caldris.other" : source.campaign.id,
        name: second ? "Other campaign" : "Fixture",
        status: "active", title: second ? "Other campaign" : "Fixture",
        premise: "A bounded campaign.", partyGoals: ["Continue."],
        toneAndBoundaries: ["Keep it safe."], rulesetScope: "dnd2024",
        creationMethod: "manual", reviewFingerprint: "a".repeat(64),
      };
      return response({
        applicationId: "dnd2024", stateSpaceId: "state.fixture",
        qualifiedQueryId: worldCampaignDirectoryContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: worldCampaignDirectoryContract.outputSchemaHash,
        resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
        data: {
          worldSummary: "A fixture world.", selectedWorld: { id: "world.caldris", name: "Caldris" },
          campaigns: [campaign], totalCount: 2, complete: second, nextCursor: second ? null : "next",
        },
      });
    },
  });
  assert.equal(result.contextSelection.worlds.length, 1);
  assert.equal(result.contextSelection.worlds[0].campaigns.length, 2);
  assert.equal(result.contextSelection.selectedCampaignId, source.campaign.id);
  assert.equal(calls.length, 2);
  assert.ok(calls.every((target) => !target.pathname.endsWith("/entities") &&
    !target.pathname.includes("/components/")));
});

test("people discovery reuses locations and does not read faction graphs", async () => {
  const calls = [];
  const result = await readDeferredHubSection({
    origin, section: "people", source: { ...source, locationDirectory: [{ id: "location.caldris.one", name: "Place" }] },
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target);
      if (target.pathname.endsWith("/entities")) return response({
        items: [{ entityId: "actor.fixture", name: "Person" }], nextCursor: null,
      });
      if (target.pathname.endsWith("/containments")) return response({
        items: [{ containedEntityId: "actor.fixture", containerEntityId: "location.caldris.one" }], nextCursor: null,
      });
      return response({}, 404);
    },
  });
  assert.deepEqual(result.worldDirectory.people.map((person) => person.id), ["actor.fixture"]);
  assert.equal(calls.length, 4);
  assert.ok(calls.every((target) => !target.pathname.includes("relationships")));
});
