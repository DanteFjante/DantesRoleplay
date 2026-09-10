import assert from "node:assert/strict";
import test from "node:test";
import { readDeferredHubSection } from "../src/server/game-server-context.js";
import { contract as worldCampaignDirectoryContract } from "../src/server/world-campaign-directory-contract.js";
import { contract as worldLocationScopeContract } from "../src/server/world-location-scope-contract.js";
import { contract as worldLocationScopePageContract } from "../src/server/world-location-scope-page-contract.js";
import { contract as worldPeopleHoldingsPageContract } from "../src/server/world-people-holdings-page-contract.js";
import { contract as currentSceneContract } from "../src/server/current-scene-contract.js";
import { contract as campaignResumeContract } from "../src/server/campaign-resume-contract.js";

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

test("deferred locations read the complete authorized hierarchy and suppress ambient media in preview", async () => {
  const calls = [];
  const locations = Array.from({ length: 2 }, (_, index) => ({
    id: `place-${index}`, name: `Place ${index}`, parentId: "world.caldris", slot: "region",
    kind: "region", status: index % 2 ? "draft" : "active", summary: "A place.",
    visibility: "public", mapAnchor: null,
  }));
  const result = await readDeferredHubSection({
    origin, section: "locations", source: { ...source, audience: { seat: "dm", perspective: "player" } },
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target);
      const scopeId = decodeURIComponent(target.pathname.split("/entities/")[1].split("/read-models/")[0]);
      const root = scopeId === "world.caldris";
      return response({
        applicationId: "dnd2024", stateSpaceId: "state.fixture",
        qualifiedQueryId: worldLocationScopePageContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: worldLocationScopePageContract.outputSchemaHash,
        resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
        data: {
          version: 1, state: "ready",
          scope: root
            ? { id: "world.caldris", name: "Caldris", parentId: null, slot: "", kind: "world",
              status: "active", summary: "A gentle world.", visibility: "public", mapAnchor: null }
            : locations.find((location) => location.id === scopeId),
          locations: root ? locations : [], totalCount: root ? locations.length : 0,
          complete: true, nextCursor: null,
        },
      });
    },
  });
  assert.equal(result.locationDirectory.length, 3);
  assert.deepEqual(result.locationScopes.find(({ id }) => id === "world.caldris"), {
    id: "world.caldris", name: "Caldris", parentId: null,
    childIds: locations.map((location) => location.id), totalCount: 2,
    complete: true, nextCursor: null, sourceRevisionFingerprint: "4".repeat(64),
  });
  assert.ok(locations.every((location) =>
    result.locationScopes.find(({ id }) => id === location.id)?.childIds.length === 0));
  assert.equal(calls.length, 3);
  assert.match(calls[0].pathname, /entities\/world\.caldris\/read-models\/dnd2024\.query\.world-location-scope-page$/u);
  assert.ok(calls.every((target) => !target.pathname.endsWith("/entities") && !target.pathname.endsWith("/media")));
});

test("failed or incompatible root scopes never produce empty location success", async () => {
  for (const value of [response({}, 500), response({ unexpected: true })]) {
    await assert.rejects(readDeferredHubSection({
      origin, source, section: "locations", fetchImpl: async () => value,
    }), /scope/);
  }
});

for (const mediaCase of [
  { name: "DM illustration", seat: "dm", perspective: "dm", visible: true },
  { name: "Actor illustration", seat: "player", perspective: "player", visible: true },
  { name: "Player preview", seat: "dm", perspective: "player", preview: true },
  { name: "missing illustration", seat: "dm", perspective: "dm", empty: true },
  { name: "denied illustration", seat: "dm", perspective: "dm", denied: true },
  { name: "wrong media owner", seat: "dm", perspective: "dm", foreign: true },
]) test(`Current cross-checks its scene and resolves exact location media: ${mediaCase.name}`, async () => {
  const calls = [];
  const imageUrl = "/api/applications/dnd2024/state-spaces/state.fixture/entities/location.caldris.one/media/setting/content";
  const result = await readDeferredHubSection({
    origin, section: "current",
    source: { ...source, knowledge: { status: "empty", entries: [], locations: [] },
      audience: { seat: mediaCase.seat, perspective: mediaCase.perspective },
      locationDirectory: [{ id: "location.caldris.one", name: "Place",
        ...(!mediaCase.visible ? { media: { setting: { imageUrl: "/stale-private-image.png" } } } : {}),
      }] },
    fetchImpl: async (input, init) => {
      const target = new URL(input); calls.push(target);
      if (target.pathname.endsWith("/media-batch")) {
        assert.deepEqual(JSON.parse(init.body), {
          entityIds: ["location.caldris.one"], perspective: mediaCase.perspective,
        });
        if (mediaCase.denied) return response({}, 403);
        return response({ applicationId: source.applicationId, stateSpaceId: source.stateSpaceId,
          items: [{ entityId: mediaCase.foreign ? "another-location" : "location.caldris.one",
            attachments: mediaCase.empty ? [] : [{ mediaId: "setting", role: "setting",
              mediaType: "image/png", width: 800, height: 600, alt: "The current place",
              caption: "", order: 0, contentUrl: imageUrl }],
          }],
        });
      }
      const resume = target.pathname.endsWith(campaignResumeContract.id);
      const contract = resume ? campaignResumeContract : currentSceneContract;
      const data = resume ? {
        version: 1,
        campaign: { id: source.campaign.id, name: "Fixture", title: "Fixture",
          premise: "Continue the fixture.", partyGoals: ["Continue."], toneAndBoundaries: ["Keep it safe."] },
        party: { activeMemberCount: 1 },
        scene: { locationId: "location.caldris.one", conversationId: null, encounterId: null },
        activeArc: null, activeChapter: null, activeSession: null, latestRecap: null,
        affordances: [{ key: "look-around", label: "Look around", summary: "Survey the area." }],
      } : {
        version: 1, kind: "exploration",
        location: { id: "location.caldris.one", kind: "site", summary: "A place.", visibility: "party" },
        conversationId: null, encounterId: null,
        affordances: [{ key: "look-around", label: "Look around", summary: "Survey the area." }],
      };
      return response({
        applicationId: "dnd2024", stateSpaceId: "state.fixture", qualifiedQueryId: contract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: contract.outputSchemaHash, resultFingerprint: "3".repeat(64),
        sourceRevisionFingerprint: "4".repeat(64), data,
      });
    },
  });
  assert.equal(result.currentSituation.status, "ready");
  assert.equal(result.currentSituation.locationId, "location.caldris.one");
  assert.deepEqual(result.currentSituation.affordances, [
    { key: "look-around", label: "Look around", summary: "Survey the area." },
  ]);
  assert.equal(calls.length, 2 + (mediaCase.preview ? 0 : 1) + (mediaCase.perspective === "dm" ? 1 : 0));
  assert.equal(result.locationDirectory[0].media?.setting?.imageUrl, mediaCase.visible ? imageUrl : undefined);
  assert.equal(calls.filter((target) => target.pathname.endsWith("/media-batch")).length, mediaCase.preview ? 0 : 1);
  assert.equal(calls.filter((target) => target.pathname.includes("/read-models/")).length, 2);
  const routeLookup = calls.find((target) => target.pathname.endsWith("/relationships"));
  if (mediaCase.perspective === "dm") {
    assert.equal(routeLookup.searchParams.get("toEntityId"), "location.caldris.one");
    assert.equal(routeLookup.searchParams.get("qualifiedKind"), "game.core.world.route.from");
  } else assert.equal(routeLookup, undefined);
  assert.ok(calls.every((target) =>
    !target.pathname.includes("/components/") && !target.pathname.endsWith("/containment")));
});

test("context discovery follows every registered continuation without hydrating entities", async () => {
  const calls = [];
  const result = await readDeferredHubSection({
    origin, section: "context", source: { ...source, world: { id: "world.caldris", name: "Caldris" } },
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
          worldSummary: "A fixture world.",
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

test("DM people and holdings use one source-bound page and one authorized media batch", async () => {
  const calls = [];
  const result = await readDeferredHubSection({
    origin, section: "people", source: { ...source, locationDirectory: [{ id: "location.caldris.one", name: "Place" }] },
    fetchImpl: async (input, init = {}) => {
      const target = new URL(input); calls.push(target);
      if (target.pathname.endsWith("/media-batch")) {
        assert.deepEqual(JSON.parse(init.body), { entityIds: ["subject-9"], perspective: "dm" });
        return response({ applicationId: "dnd2024", stateSpaceId: "state.fixture", items: [] });
      }
      return response({
        applicationId: "dnd2024", stateSpaceId: "state.fixture",
        qualifiedQueryId: worldPeopleHoldingsPageContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: worldPeopleHoldingsPageContract.outputSchemaHash,
        resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
        data: {
          version: 1, state: "ready", world: { id: "world.caldris", name: "Caldris" },
          locations: [{ id: "place-azure", name: "Azure Reach", parentId: "world.caldris",
            kind: "region" }],
          people: [{ id: "subject-9", name: "Person", locationId: "place-azure", kind: "NPC",
            motive: null }],
          holdings: [{ id: "plain-identity", name: "Rope", locationId: "place-azure", kind: "Item" }],
          totalCount: 2, complete: true, nextCursor: null,
          limits: { contentsDepth: 16, recordCount: 2000, pageSize: 50, hierarchyComplete: true },
        },
      });
    },
  });
  assert.deepEqual(result.worldDirectory.people.map((person) => person.id), ["subject-9"]);
  assert.deepEqual(result.worldDirectory.holdings.map((holding) => holding.id), ["plain-identity"]);
  assert.equal(calls.length, 2);
  assert.match(calls[0].pathname,
    /entities\/world\.caldris\/read-models\/dnd2024\.query\.world-people-holdings-page$/u);
  assert.ok(calls.every((target) => !/\/(entities|containments)$/u.test(target.pathname) &&
    !target.pathname.includes("/components/") && !target.pathname.includes("relationships") &&
    !target.pathname.endsWith("/media")));
});

test("Actor People reuse only known lore and attach permitted media in one batch", async () => {
  const calls = [];
  const actorSource = { ...source, audience: { seat: "player", perspective: "player" } };
  const result = await readDeferredHubSection({
    origin, section: "people", source: actorSource,
    fetchImpl: async (input, init = {}) => {
      const target = new URL(input); calls.push(target);
      if (target.pathname.endsWith("/knowledge")) return response({
        status: "ready", entries: [{ text: "Mara runs the harbour.", stance: "known",
          presentationKind: "person", mediaOwnerId: "subject-9" }], locations: [],
      });
      assert.ok(target.pathname.endsWith("/media-batch"));
      assert.deepEqual(JSON.parse(init.body), { entityIds: ["subject-9"], perspective: "player" });
      return response({ applicationId: "dnd2024", stateSpaceId: "state.fixture", items: [] });
    },
  });
  assert.deepEqual(result.knowledge.entries, [{
    text: "Mara runs the harbour.", stance: "known", presentationKind: "person",
  }]);
  assert.equal(calls.length, 2);
  assert.ok(calls.every((target) => !target.pathname.includes("world-people-holdings") &&
    !target.pathname.endsWith("/media")));
});

test("empty DM People is distinct from a denied directory", async () => {
  const envelope = (data) => ({
    applicationId: "dnd2024", stateSpaceId: "state.fixture",
    qualifiedQueryId: worldPeopleHoldingsPageContract.id,
    stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
    outputSchemaHash: worldPeopleHoldingsPageContract.outputSchemaHash,
    resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64), data,
  });
  const empty = await readDeferredHubSection({
    origin, section: "people", source,
    fetchImpl: async () => response(envelope({
      version: 1, state: "ready", world: { id: "world.caldris", name: "Caldris" },
      locations: [], people: [], holdings: [],
      totalCount: 0, complete: true, nextCursor: null,
      limits: { contentsDepth: 16, recordCount: 2000, pageSize: 50, hierarchyComplete: true },
    })),
  });
  assert.deepEqual(empty.worldDirectory.people, []);
  assert.deepEqual(empty.worldDirectory.holdings, []);

  await assert.rejects(readDeferredHubSection({
    origin, section: "people", source,
    fetchImpl: async () => response(envelope({
      version: 1, state: "forbidden", world: null, locations: [], people: [], holdings: [],
      totalCount: 0, complete: true, nextCursor: null,
      limits: { contentsDepth: 16, recordCount: 2000, pageSize: 50, hierarchyComplete: true },
    })),
  }), /unavailable to this audience/u);
});
