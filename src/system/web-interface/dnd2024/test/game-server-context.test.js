import assert from "node:assert/strict";
import test from "node:test";
import { boardEnvelope } from "./fixtures/encounter-board.js";
import { connectedCampaignToHubEnvelope } from "../src/server/connected-hub-envelope.ts";
import { resolveHubSurface } from "../src/data/hub-availability.js";
import { contract as campaignSummaryContract } from "../src/server/campaign-summary-contract.js";
import { contract as campaignContextContract } from "../src/server/campaign-context-contract.js";
import { contract as campaignDetailsContract } from "../src/server/campaign-details-contract.js";
import { contract as characterSheetContract } from "../src/server/character-sheet-contract.js";
import { contract as characterDossierContract } from "../src/server/character-dossier-contract.js";
import { contract as factionDirectoryContract } from "../src/server/faction-directory-contract.js";
import { contract as inventoryContainerContract } from "../src/server/inventory-container-contract.js";
import { contract as worldLocationScopeContract } from "../src/server/world-location-scope-contract.js";
import { contract as worldLocationScopePageContract } from "../src/server/world-location-scope-page-contract.js";
import { contract as campaignResumeContract } from "../src/server/campaign-resume-contract.js";
import { contract as currentSceneContract } from "../src/server/current-scene-contract.js";

import {
  inheritMediaVisual,
  normalizeGameServerOrigin,
  projectMediaVisual,
  readCombatCurrentScene,
  readCanonicalCharacter,
  readCanonicalCharacterSheet,
  readCanonicalInventory,
  readConversationCurrentScene,
  readGameServerContext,
  readRegisteredCampaignSummary,
  readRegisteredCampaignDetails,
  readRegisteredCurrentPlay,
  readRegisteredFactionDirectoryPage,
  readRegisteredWorldLocationScope,
  readRegisteredWorldLocationScopePage,
  readWorldLocationScopePatch,
  readDeferredHubSection,
  readKnownOpenRoutes,
  resolveRecordedPlaySituation,
} from "../src/server/game-server-context.js";

function worldLocationScopeData(scopeId = "realm-root-7") {
  return {
    version: 1,
    state: "ready",
    scope: {
      id: scopeId, name: "The Seventh Realm", parentId: null, slot: "", kind: "world",
      status: "active", summary: "A renamed world with no identifier convention.", visibility: "party",
      mapAnchor: null,
    },
    locations: [{
      id: "place-azure", name: "Azure Reach", parentId: scopeId, slot: "region", kind: "region",
      status: "active", summary: "The coast beneath blue cliffs.", visibility: "public",
      mapAnchor: { x: 125, y: 875 },
    }],
    limits: { contentsDepth: 1, locationCount: 100, complete: true },
  };
}

function worldScopeEnvelope(data) {
  return {
    applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    qualifiedQueryId: worldLocationScopeContract.id,
    stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
    outputSchemaHash: worldLocationScopeContract.outputSchemaHash, resultFingerprint: "3".repeat(64),
    sourceRevisionFingerprint: "4".repeat(64), data,
  };
}

function worldLocationScopePageData(scopeId = "realm-root-7", locations = worldLocationScopeData(scopeId).locations,
  { totalCount = locations.length, complete = true, nextCursor = null } = {}) {
  return {
    version: 1,
    state: "ready",
    scope: worldLocationScopeData(scopeId).scope,
    locations,
    totalCount,
    complete,
    nextCursor,
  };
}

function worldScopePageEnvelope(data, sourceRevisionFingerprint = "4".repeat(64)) {
  return {
    applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    qualifiedQueryId: worldLocationScopePageContract.id,
    stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
    outputSchemaHash: worldLocationScopePageContract.outputSchemaHash, resultFingerprint: "3".repeat(64),
    sourceRevisionFingerprint, data,
  };
}

function currentPlayEnvelope(contract, data, sourceRevisionFingerprint = "a".repeat(64)) {
  return {
    applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    qualifiedQueryId: contract.id,
    stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
    outputSchemaHash: contract.outputSchemaHash, resultFingerprint: "3".repeat(64),
    sourceRevisionFingerprint, data,
  };
}

function campaignResumeData(scene, affordances = []) {
  return {
    version: 1,
    campaign: {
      id: "campaign.thalorien", name: "Thalorien", title: "The Broken Crown",
      premise: "Keep the realm from falling apart.", partyGoals: ["Protect Brackenford."],
      toneAndBoundaries: ["Heroic fantasy."],
    },
    party: { activeMemberCount: 1 }, scene,
    activeArc: null, activeChapter: null, activeSession: null, latestRecap: null,
    affordances,
  };
}

function currentSceneData(kind, affordances = []) {
  return {
    version: 1, kind,
    location: {
      id: "location.thalorien.brackenford", kind: "settlement",
      summary: "A guarded frontier town.", visibility: "party",
    },
    conversationId: kind === "conversation" ? "interaction.brackenford.parley" : null,
    encounterId: kind === "combat" ? "encounter.brackenford.ambush" : null,
    affordances,
  };
}

test("world location scope reads a non-conventional exact identity and batches only returned media owners", async () => {
  const calls = [];
  const result = await readRegisteredWorldLocationScope({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    scopeId: "realm-root-7", perspective: "player",
    fetchImpl: async (input, init = {}) => {
      calls.push({ url: new URL(input), init });
      if (new URL(input).pathname.endsWith("/media-batch")) return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main", items: [{
          entityId: "realm-root-7", attachments: [{
            mediaId: "map.realm-7", role: "map", mediaType: "image/webp", width: 1000, height: 1000,
            alt: "Map of the Seventh Realm", caption: "", order: 0,
            contentUrl: "/api/applications/dnd2024/state-spaces/dnd2024-main/media/map.realm-7/content",
          }],
        }],
      });
      return response(200, worldScopeEnvelope(worldLocationScopeData()));
    },
  });
  assert.equal(result.status, "ready");
  assert.deepEqual(result.items.map((item) => [item.id, item.containerId ?? null]), [
    ["realm-root-7", null], ["place-azure", "realm-root-7"],
  ]);
  assert.equal(result.items[0].isWorldRoot, true);
  assert.match(result.items[0].mapVisual.imageUrl, /map\.realm-7\/content$/u);
  assert.deepEqual({ width: result.items[0].mapVisual.width, height: result.items[0].mapVisual.height },
    { width: 1000, height: 1000 });
  assert.equal(result.items[0].mapVisualState, "ready");
  assert.equal(result.items[1].mapVisualState, "absent");
  assert.equal(calls.length, 2);
  assert.match(calls[0].url.pathname, /entities\/realm-root-7\/read-models\/dnd2024\.query\.world-location-scope$/u);
  assert.deepEqual(JSON.parse(calls[1].init.body).entityIds, ["realm-root-7", "place-azure"]);
  assert.ok(calls.every(({ url }) => !/\/entities$/u.test(url.pathname)));
});

test("a failed location media batch remains unavailable instead of becoming false map absence", async () => {
  const result = await readRegisteredWorldLocationScope({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    scopeId: "realm-root-7", perspective: "player",
    fetchImpl: async (input) => new URL(input).pathname.endsWith("/media-batch")
      ? response(503, {})
      : response(200, worldScopeEnvelope(worldLocationScopeData())),
  });
  assert.equal(result.status, "ready");
  assert.deepEqual(result.items.map((item) => item.mapVisualState), ["unavailable", "unavailable"]);
  assert.equal(result.items.some((item) => item.mapVisual), false);
});

test("paged location scopes bind continuation to the first page source revision", async () => {
  const firstLocations = Array.from({ length: 100 }, (_, index) => ({
    id: `place-${String(index).padStart(3, "0")}`,
    name: `Place ${String(index).padStart(3, "0")}`,
    parentId: "realm-root-7", slot: "region", kind: "region", status: "active",
    summary: "A reachable place.", visibility: "public", mapAnchor: null,
  }));
  const calls = [];
  const first = await readRegisteredWorldLocationScopePage({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    scopeId: "realm-root-7", perspective: "player", includeMedia: false,
    fetchImpl: async (input) => {
      calls.push(new URL(input));
      return response(200, worldScopePageEnvelope(worldLocationScopePageData(
        "realm-root-7", firstLocations, { totalCount: 101, complete: false, nextCursor: "100" }),
      ));
    },
  });
  assert.equal(first.status, "ready");
  assert.equal(first.items.length, 100);
  assert.equal(first.nextCursor, "100");
  assert.deepEqual(JSON.parse(calls[0].searchParams.get("input")), {
    offset: 0, expectedSourceRevision: null,
  });

  const expected = first.projection.sourceRevisionFingerprint;
  const second = await readRegisteredWorldLocationScopePage({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    scopeId: "realm-root-7", perspective: "player", includeMedia: false,
    cursor: "100", expectedSourceRevision: expected,
    fetchImpl: async (input) => {
      const target = new URL(input);
      assert.deepEqual(JSON.parse(target.searchParams.get("input")), {
        offset: 100, expectedSourceRevision: expected,
      });
      return response(200, worldScopePageEnvelope(worldLocationScopePageData("realm-root-7", [{
        id: "place-100", name: "Place 100", parentId: "realm-root-7", slot: "region",
        kind: "region", status: "active", summary: "The last place.", visibility: "public",
        mapAnchor: null,
      }], { totalCount: 101 }), expected));
    },
  });
  assert.equal(second.status, "ready");
  assert.deepEqual(second.items.map((item) => item.id), ["place-100"]);

  const changed = await readRegisteredWorldLocationScopePage({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    scopeId: "realm-root-7", perspective: "player", includeMedia: false,
    cursor: "100", expectedSourceRevision: expected,
    fetchImpl: async () => response(200, worldScopePageEnvelope(
      worldLocationScopePageData("realm-root-7", [{
        id: "place-100", name: "Place 100", parentId: "realm-root-7", slot: "region",
        kind: "region", status: "active", summary: "Changed source.", visibility: "public",
        mapAnchor: null,
      }], { totalCount: 101 }), "9".repeat(64))),
  });
  assert.equal(changed.status, "stale");
});

test("scope refresh replaces deleted membership and reconciles a moved location", async () => {
  const rawLocation = (id, name, parentId) => ({
    id, name, parentId, slot: "region", kind: "region", status: "active",
    summary: `${name} summary.`, visibility: "public", mapAnchor: null,
  });
  const source = {
    applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    audience: { seat: "dm", perspective: "player" },
    contextSelection: { selectedWorldId: "realm-root-7" },
    locationDirectoryAudience: "player",
    locationDirectory: [
      { id: "realm-root-7", name: "The Seventh Realm", kind: "world", isWorldRoot: true },
      { id: "atlas", name: "Atlas", kind: "region", containerId: "realm-root-7" },
      { id: "moved", name: "Moved Place", kind: "region", containerId: "atlas" },
      { id: "deleted", name: "Deleted Place", kind: "region", containerId: "realm-root-7" },
      { id: "deep", name: "Deleted Child", kind: "site", containerId: "deleted" },
    ],
    locationScopes: [
      { id: "realm-root-7", name: "The Seventh Realm", parentId: null,
        childIds: ["atlas", "deleted"], totalCount: 2, complete: true, nextCursor: null,
        sourceRevisionFingerprint: "1".repeat(64) },
      { id: "atlas", name: "Atlas", parentId: "realm-root-7",
        childIds: ["moved"], totalCount: 1, complete: true, nextCursor: null,
        sourceRevisionFingerprint: "2".repeat(64) },
      { id: "deleted", name: "Deleted Place", parentId: "realm-root-7",
        childIds: ["deep"], totalCount: 1, complete: true, nextCursor: null,
        sourceRevisionFingerprint: "3".repeat(64) },
    ],
  };
  const locations = [rawLocation("atlas", "Atlas", "realm-root-7"),
    rawLocation("moved", "Moved Place", "realm-root-7")];
  const patch = await readWorldLocationScopePatch({
    origin: "http://localhost:6217", source, scopeId: "realm-root-7",
    fetchImpl: async () => response(200, worldScopePageEnvelope(
      worldLocationScopePageData("realm-root-7", locations, { totalCount: 2 }))),
  });
  assert.deepEqual(new Set(patch.locationDirectory.map((item) => item.id)),
    new Set(["realm-root-7", "atlas", "moved"]));
  assert.deepEqual(patch.locationScopes.find((scope) => scope.id === "realm-root-7").childIds,
    ["atlas", "moved"]);
  assert.deepEqual(patch.locationScopes.find((scope) => scope.id === "atlas").childIds, []);
  assert.equal(patch.locationDirectory.find((item) => item.id === "moved").containerId, "realm-root-7");
  assert.equal(patch.locationScopes.some((scope) => scope.id === "deleted"), false);
});

test("World and Locations deferred view uses one authorized root scope and no raw directory scan", async () => {
  const calls = [];
  const source = {
    applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    audience: { seat: "dm", perspective: "player", allowedPerspectives: ["dm", "player"] },
    contextSelection: { selectedWorldId: "realm-root-7", selectedCampaignId: "campaign-9", worlds: [] },
    campaign: { id: "campaign-9" },
  };
  const patch = await readDeferredHubSection({
    origin: "http://localhost:6217", source, section: "locations",
    fetchImpl: async (input) => {
      calls.push(new URL(input));
      return response(200, worldScopePageEnvelope(worldLocationScopePageData()));
    },
  });
  assert.deepEqual(new Set(patch.locationDirectory.map((item) => item.id)), new Set(["realm-root-7", "place-azure"]));
  assert.deepEqual(patch.locationScopes, [{
    id: "realm-root-7", name: "The Seventh Realm", parentId: null,
    childIds: ["place-azure"], totalCount: 1, complete: true, nextCursor: null,
    sourceRevisionFingerprint: "4".repeat(64),
  }]);
  assert.equal(patch.locationDirectoryAudience, "player");
  assert.equal(calls.length, 1);
  assert.ok(calls.every((call) => !/\/entities$/u.test(call.pathname) && !call.pathname.endsWith("/media")));
});

function inventoryContainerData(actorId) {
  return {
    version: 1,
    owner: { id: actorId, label: "Ganji" },
    state: "ready",
    reasons: [],
    items: [
      {
        id: "inventory.backpack", name: "Backpack", definition: { id: "item.backpack", label: "Backpack" },
        quantity: 1, slot: "carried", parentItemId: null, order: 0, depth: 1, childCount: 1,
        deeperContentsOmitted: false, equipmentSlots: [], classification: "item",
      },
      {
        id: "inventory.rope", name: "Hempen rope", definition: { id: "item.rope", label: "Hempen rope" },
        quantity: 1, slot: "contained", parentItemId: "inventory.backpack", order: 0, depth: 2,
        childCount: 0, deeperContentsOmitted: false, equipmentSlots: [], classification: "item",
      },
    ],
    wallet: { coinCount: 3, copperValue: 300, gpCount: 3, denominations: [
      { denomination: { id: "currency.gp", label: "Gold piece" }, code: "gp", count: 3,
        copperValuePerCoin: 100, totalCopperValue: 300 },
    ] },
    limits: { contentsDepth: 4, itemCount: 100, complete: true },
  };
}

test("inventory container reads one bounded nested projection without item-tab fan-out", async () => {
  const calls = [];
  const actorId = "actor.ganji";
  const result = await readCanonicalInventory({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    actorId, perspective: "player",
    fetchImpl: async (input) => {
      calls.push(new URL(input));
      return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main", qualifiedQueryId: inventoryContainerContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: inventoryContainerContract.outputSchemaHash, resultFingerprint: "3".repeat(64),
        sourceRevisionFingerprint: "4".repeat(64), data: inventoryContainerData(actorId),
      });
    },
  });
  assert.equal(result.status, "ready");
  assert.deepEqual(result.data.items.map((item) => [item.id, item.parentItemId]), [
    ["inventory.backpack", null], ["inventory.rope", "inventory.backpack"],
  ]);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].searchParams.get("perspective"), "player");
  assert.match(calls[0].pathname, /dnd2024\.query\.inventory-container$/);
  assert.ok(calls.every((call) => !/recipes|uses|character-dossier/.test(call.pathname)));
});

test("inventory container rejects cycles and preserves authorization failures", async () => {
  const actorId = "actor.ganji";
  const cycle = inventoryContainerData(actorId);
  cycle.items[0].parentItemId = "inventory.rope";
  cycle.items[0].depth = 3;
  const incompatible = await readCanonicalInventory({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    actorId, perspective: "dm", fetchImpl: async () => response(200, {
      applicationId: "dnd2024", stateSpaceId: "dnd2024-main", qualifiedQueryId: inventoryContainerContract.id,
      stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
      outputSchemaHash: inventoryContainerContract.outputSchemaHash, resultFingerprint: "3".repeat(64),
      sourceRevisionFingerprint: "4".repeat(64), data: cycle,
    }),
  });
  assert.equal(incompatible.status, "error");
  assert.equal(incompatible.failureCategory, "incompatible-data");

  const forbidden = await readCanonicalInventory({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    actorId, perspective: "player", fetchImpl: async () => response(403, { code: "forbidden" }),
  });
  assert.equal(forbidden.status, "forbidden");
  assert.equal(forbidden.failureCategory, "authorization");
});

test("registered Campaign summary stays bounded and preserves read-only party references", async () => {
  const calls = [];
  const summary = await readRegisteredCampaignSummary({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    campaignId: "campaign.caldris.measure-of-mercy", perspective: "player",
    fetchImpl: async (input) => {
      calls.push(new URL(input));
      return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
        qualifiedQueryId: campaignSummaryContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: campaignSummaryContract.outputSchemaHash, resultFingerprint: "4".repeat(64),
        sourceRevisionFingerprint: "5".repeat(64), data: {
        status: "active", title: "The Measure of Mercy", premise: "Choose what mercy costs.",
        partyGoals: ["Protect Ganji."], toneAndBoundaries: ["No sexual violence."],
        party: [{ id: "participation.ganji", name: "Ganji participation", status: "active" }],
        totalCount: 1, complete: true, nextCursor: null,
      } });
    },
  });
  assert.equal(summary.title, "The Measure of Mercy");
  assert.equal(summary.projection.sourceRevisionFingerprint, "5".repeat(64));
  assert.equal(summary.projection.resolutionFingerprint, "2".repeat(64));
  assert.deepEqual(summary.party.map((entry) => entry.id), ["participation.ganji"]);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].searchParams.get("perspective"), "player");
  assert.ok(calls.every((call) => !call.pathname.includes("knowledge") && !call.pathname.includes("inventory")));
});

test("Player Campaign details reject GM fields and session records", async () => {
  const load = (data) => readRegisteredCampaignDetails({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    campaignId: "campaign.caldris.measure-of-mercy", perspective: "player",
    fetchImpl: async () => response(200, {
      applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
      qualifiedQueryId: campaignDetailsContract.id,
      stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
      outputSchemaHash: campaignDetailsContract.outputSchemaHash, resultFingerprint: "3".repeat(64),
      sourceRevisionFingerprint: "4".repeat(64), data,
    }),
  });
  const visible = await load({
    version: 1, campaignId: "campaign.caldris.measure-of-mercy",
    chapters: [{ id: "chapter.one", name: "One", status: "active", title: "One",
      partyQuestion: "What now?" }], arcs: [], sessions: [],
  });
  assert.equal(visible.chapters.length, 1);
  assert.equal(await load({
    version: 1, campaignId: "campaign.caldris.measure-of-mercy",
    chapters: [{ id: "chapter.one", name: "One", status: "active", title: "One",
      partyQuestion: "What now?", gmContext: "Secret" }], arcs: [], sessions: [],
  }), null);
  assert.equal(await load({
    version: 1, campaignId: "campaign.caldris.measure-of-mercy", chapters: [], arcs: [],
    sessions: [{ id: "session.one", name: "One", status: "active", ordinal: 1 }],
  }), null);
});

test("registered faction pages stay bounded and do not fan out into knowledge or inventory reads", async () => {
  const calls = [];
  const page = await readRegisteredFactionDirectoryPage({
    origin: "http://localhost:6217",
    applicationId: "dnd2024",
    stateSpaceId: "dnd2024-main",
    worldId: "world.caldris",
    fetchImpl: async (input) => {
      const request = new URL(input);
      calls.push(request.pathname + request.search);
      return response(200, {
        applicationId: "dnd2024",
        stateSpaceId: "dnd2024-main",
        qualifiedQueryId: factionDirectoryContract.id,
        stateSpaceFingerprint: "1".repeat(64),
        resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: factionDirectoryContract.outputSchemaHash,
        resultFingerprint: "4".repeat(64),
        sourceRevisionFingerprint: "A".repeat(64),
        data: {
          worldSummary: "A low-magic world shaped by roads, rivers, and rival powers.",
          items: [{
            id: "faction.caldris.tensor-sect", name: "Tensor Sect", status: "active",
            visibility: "public", summary: "A disciplined order.", goals: ["Perfect perception."],
            methods: ["Study."], assets: [], agenda: { state: "ready", summary: "Retrieve Ganji." },
            members: [{ id: "actor.caldris.ganji", name: "Ganji" }],
            controlledSites: [{ id: "location.caldris.ninth-angle", name: "House of the Ninth Angle" }],
            territories: [{ id: "location.caldris.highmead", name: "Highmead" }],
            allies: [], opponents: [],
          }],
          totalCount: 35, complete: false, nextCursor: "next-page",
        },
      });
    },
  });

  assert.equal(page.factions.length, 1);
  assert.equal(page.projection.resolutionFingerprint, "2".repeat(64));
  assert.equal(page.totalCount, 35);
  assert.equal(page.factions[0].agenda.summary, "Retrieve Ganji.");
  assert.deepEqual(page.factions[0].memberIds, ["actor.caldris.ganji"]);
  assert.deepEqual(page.factions[0].territoryIds,
    ["location.caldris.ninth-angle", "location.caldris.highmead"]);
  assert.equal(calls.length, 1);
  assert.match(calls[0], /dnd2024\.query\.faction-directory-page/u);
  assert.match(calls[0], /world\.caldris/u);
  assert.ok(calls.every((call) => !call.includes("knowledge") && !call.includes("inventory")));

  const denied = await readRegisteredFactionDirectoryPage({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    worldId: "world.caldris", fetchImpl: async () => response(403, {}),
  });
  assert.equal(denied, null);
});

const partyReference = (index) => ({
  id: `participation.${index}`, name: `Participation ${index}`, status: "active",
  actors: [{ id: `actor.${index}`, name: `Actor ${index}` }],
});

async function readRegisteredPartyBootstrap({
  party = [partyReference(0)], summary = {}, perspective = "dm", role = "game-master",
  queryResponse, localSeat, shared = false,
} = {}) {
  const calls = [];
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217", requestedPerspective: perspective, localSeat,
    fetchImpl: async (input) => {
      const request = new URL(input); calls.push(request.pathname);
      if (request.pathname === "/api/audience-context") {
        const result = response(200, {
        status: "bound", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
        campaignId: "campaign.caldris.measure-of-mercy", role,
        ...(role === "actor" ? { actorId: "actor.0" } : {}),
      });
        if (shared) result.headers.set("X-Website-Access", "shared");
        return result;
      }
      if (role === "actor" && request.pathname.endsWith("/entities/actor.0"))
        return response(200, { entityId: "actor.0", name: "Actor 0" });
      if (request.pathname.includes("dnd2024.query.campaign-context")) {
        return response(200, {
          applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
          qualifiedQueryId: campaignContextContract.id,
          stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
          outputSchemaHash: campaignContextContract.outputSchemaHash, resultFingerprint: "3".repeat(64),
          sourceRevisionFingerprint: "4".repeat(64), data: {
            version: 1,
            campaignId: "campaign.caldris.measure-of-mercy",
            worldId: "world.caldris",
            campaign: { id: "campaign.caldris.measure-of-mercy", name: "The Measure of Mercy" },
            world: { id: "world.caldris", name: "Caldris" },
          },
        });
      }
      if (request.pathname.includes("dnd2024.query.campaign-summary")) {
        if (queryResponse) return queryResponse();
        return response(200, {
          applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
          qualifiedQueryId: campaignSummaryContract.id,
          stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
          outputSchemaHash: campaignSummaryContract.outputSchemaHash, resultFingerprint: "4".repeat(64),
          sourceRevisionFingerprint: "5".repeat(64), data: {
            status: "active", title: "The Measure of Mercy", premise: "Choose what mercy costs.",
            partyGoals: ["Protect the party."], toneAndBoundaries: ["No sexual violence."],
            party, totalCount: party.length, complete: true, nextCursor: null, ...summary,
          },
        });
      }
      assert.fail("Unexpected per-member or directory read: " + request.pathname);
    },
  });
  return { value, calls };
}

test("shared website ignores obsolete Player preferences and exposes the complete table", async () => {
  const { value, calls } = await readRegisteredPartyBootstrap({ shared: true, perspective: "player" });
  assert.equal(value.status, "connected");
  assert.deepEqual(value.audience, { seat: "dm", perspective: "dm", allowedPerspectives: ["dm"] });
  const hub = connectedCampaignToHubEnvelope(value);
  assert.equal(resolveHubSurface(hub), "table");
  assert.equal(hub.party.length, 1);
  assert.equal(calls.length, 3);
});

test("failed bootstrap and source drift are service failures, not restricted access", async () => {
  const offline = await readGameServerContext({ serverOrigin: "http://98.128.172.181",
    fetchImpl: async () => { throw new TypeError("Failed to fetch"); } });
  assert.equal(resolveHubSurface(offline), "connection");
  const { value } = await readRegisteredPartyBootstrap({ shared: true,
    queryResponse: () => response(503, { code: "SOURCE_FILE_DRIFT" }) });
  assert.equal(resolveHubSurface(value), "unavailable");
  const denied = await readGameServerContext({ serverOrigin: "http://98.128.172.181",
    fetchImpl: async () => response(403, { status: "denied" }) });
  assert.equal(resolveHubSurface(denied), "denied");
});

test("registered bootstrap keeps the server actor seat despite obsolete local DM input", async () => {
  const { value, calls } = await readRegisteredPartyBootstrap({
    role: "actor",
    perspective: "dm",
    localSeat: "dm",
    party: [{ ...partyReference(0), actors: [] }],
  });
  assert.deepEqual(value.audience, {
    seat: "player",
    perspective: "player",
    allowedPerspectives: ["player"],
  });
  assert.deepEqual(value.party.map((entry) => entry.id), ["actor.0"]);
  assert.equal(calls.length, 4);
  assert.ok(calls.some((path) => path.endsWith(
    "/entities/actor.0/read-models/dnd2024.query.campaign-context",
  )));
});

for (const count of [0, 1, 3, 20]) {
  test(`registered Campaign bootstrap batches all ${count} members in three HTTP reads`, async () => {
    const party = Array.from({ length: count }, (_, index) => partyReference(index));
    const { value, calls } = await readRegisteredPartyBootstrap({ party });
    assert.equal(value.status, "connected");
    assert.deepEqual(value.party.map(entry => ({ id: entry.id, name: entry.name })), party.flatMap(entry => entry.actors));
    assert.equal(value.campaign.projection.sourceRevisionFingerprint, "5".repeat(64));
    assert.equal(calls.length, 3);
    assert.ok(calls.every(path => !path.endsWith("/relationships") && !path.includes("/entities/actor.")));
    if (count) {
      const projected = connectedCampaignToHubEnvelope(value);
      assert.equal(projected.party[0].sheetState.status, "idle");
      assert.equal(projected.party[0].recordStatus, "Identity only");
    }
  });
}

function assertUnavailableRoster(value) {
  assert.equal(value.status, "unavailable");
  assert.equal("party" in value, false, "A failed join must not expose a partial roster");
  assert.equal(resolveHubSurface(value), "unavailable");
}

for (const failedIndex of [0, 19]) {
  for (const [reason, mutate] of Object.entries({
    "missing actor reference": entry => { entry.actors = []; },
    "missing actor field (old contract)": entry => { delete entry.actors; },
    "ambiguous actor references": entry => { entry.actors.push({ id: "actor.other", name: "Other" }); },
    "missing actor identity": entry => { delete entry.actors[0].id; },
    "missing actor name": entry => { delete entry.actors[0].name; },
    "extra private fields": entry => { entry.actors[0].secret = "Hidden"; },
    "invalid status": entry => { entry.status = "unknown"; },
  })) {
    test(`batched roster rejects ${reason} at participation ${failedIndex + 1}`, async () => {
      const party = Array.from({ length: 20 }, (_, index) => partyReference(index));
      mutate(party[failedIndex]);
      const { value, calls } = await readRegisteredPartyBootstrap({ party });
      assertUnavailableRoster(value);
      assert.equal(calls.length, 3);
    });
  }
}

test("batched roster fails closed on failed, denied and malformed object responses", async () => {
  for (const queryResponse of [
    () => response(500, {}), () => response(403, {}),
    () => { throw new Error("Transport failure"); },
    () => ({ ok: true, status: 200, json: async () => { throw new SyntaxError("Invalid JSON"); } }),
  ]) {
    const { value, calls } = await readRegisteredPartyBootstrap({ queryResponse });
    assertUnavailableRoster(value);
    assert.equal(calls.length, 3);
  }
});

test("batched roster rejects partial, over-limit and duplicated participation records", async () => {
  for (const options of [
    { summary: { totalCount: 2, complete: false, nextCursor: "more-members" } },
    { summary: { totalCount: 2, complete: true, nextCursor: null } },
    { party: Array.from({ length: 21 }, (_, index) => partyReference(index)) },
    { party: [partyReference(0), partyReference(0)] },
  ]) {
    const { value, calls } = await readRegisteredPartyBootstrap(options);
    assertUnavailableRoster(value);
    assert.equal(calls.length, 3);
  }
});

test("batched roster deduplicates shared actors, excludes withdrawn participants and rejects conflicting names", async () => {
  const duplicate = { ...partyReference(1), actors: partyReference(0).actors };
  const withdrawn = { ...partyReference(2), status: "withdrawn", actors: [] };
  const { value, calls } = await readRegisteredPartyBootstrap({ party: [partyReference(0), duplicate, withdrawn] });
  assert.equal(value.status, "connected");
  assert.deepEqual(value.party.map(entry => entry.id), ["actor.0"]);
  assert.equal(calls.length, 3);
  const empty = await readRegisteredPartyBootstrap({ party: [withdrawn] });
  assert.deepEqual(empty.value.party, []);
  duplicate.actors = [{ id: "actor.0", name: "Conflicting name" }];
  assertUnavailableRoster((await readRegisteredPartyBootstrap({ party: [partyReference(0), duplicate] })).value);
});

for (const role of ["game-master", "actor"]) {
  test(`batched ${role} Player projection never accepts DM actor references`, async () => {
    const party = Array.from({ length: 20 }, (_, index) => ({ ...partyReference(index), actors: [] }));
    const { value, calls } = await readRegisteredPartyBootstrap({ party, role, perspective: "player" });
    assert.equal(value.status, "connected");
    assert.equal(calls.length, role === "actor" ? 4 : 3);
    assert.ok(value.campaign.party.every(entry => entry.actors.length === 0));
    if (role === "actor") assert.deepEqual(value.party.map(entry => entry.id), ["actor.0"]);
    else assert.ok(value.party.every(entry => entry.id.startsWith("participation.")));
    party[19].actors = [{ id: "actor.secret", name: "Secret" }];
    assertUnavailableRoster((await readRegisteredPartyBootstrap({ party, role, perspective: "player" })).value);
  });
}

test("Game Master Player preview excludes withdrawn participation summaries", async () => {
  const active = { ...partyReference(0), actors: [] };
  const withdrawn = { ...partyReference(1), status: "withdrawn", actors: [] };
  const { value, calls } = await readRegisteredPartyBootstrap({
    party: [active, withdrawn], role: "game-master", perspective: "player",
  });
  assert.equal(value.status, "connected");
  assert.deepEqual(value.party.map(entry => entry.id), [active.id]);
  assert.equal(calls.length, 3);
});

const MEDIA_HASH = "3ae0336e89155a4a00fb0d982ae903bf9ed1137cd292b097b252fd38c1501fa3";

function dossierDefinition(reference, kind, status = "active") {
  return {
    id: reference.id,
    label: reference.label,
    canonicalName: reference.label,
    kind,
    status,
    summary: null,
    source: status === "active"
      ? { sourceId: "dnd2024.source.srd-5.2.1", locator: `Fixture > ${reference.label}` }
      : null,
  };
}

function characterDossier(sheet) {
  const species = dossierDefinition(sheet.origin.species, "species");
  const background = dossierDefinition(sheet.origin.background, "background");
  const classes = sheet.classes.map((entry) => ({
    id: entry.id,
    name: entry.name,
    definition: dossierDefinition(entry.class, "class"),
    level: entry.level,
    subclass: entry.subclass,
  }));
  const inventoryDefinitions = sheet.inventory.items.map((entry) =>
    dossierDefinition(entry.definition, "equipment", "identity-only"));
  const definitions = [species, background, ...classes.map((entry) => entry.definition), ...inventoryDefinitions];
  return {
    version: 1,
    sheet,
    origin: { species, background, traits: [] },
    classes,
    features: [],
    inventory: { definitions: inventoryDefinitions, contentsDepth: 4, mayOmitDeeperContents: true },
    levelOneRules: {
      test: "character-level-one-rules-project",
      subjectId: sheet.subject.id,
      armorClass: {},
      attacks: [],
      senses: [],
      savingThrowCircumstances: [],
      spellAccess: {},
      equipment: {},
      entitlements: [],
    },
    definitions,
    provenance: {
      sheetQueryId: "dnd2024.query.character-sheet-v2",
      sheetProjectionId: "dnd2024.mechanic.character-sheet-v2.project",
      dossierProjectionId: "dnd2024.mechanic.character-dossier-v1.project",
      definitionCount: definitions.length,
      inventoryDepth: 4,
      ruleTextPolicy: "canonical-only",
    },
  };
}

function mediaAttachment(role = "portrait", alt = "A reviewed portrait", mediaId = "visual-0") {
  return {
    mediaId,
    role,
    mediaType: "image/png",
    width: 1024,
    height: 1536,
    alt,
    caption: "",
    order: 0,
    contentUrl: `/api/applications/dnd2024/state-spaces/dnd2024-main/entities/owner/media/${mediaId}/content`,
  };
}

test("character sheet resource reads the existing calculated query without dossier or media fan-out", async () => {
  const sheet = {
    version: 2, subject: { id: "actor.fixture", label: "Fixture Hero" },
    origin: { species: { id: "species.fixture", label: "Fixture Species" },
      background: { id: "background.fixture", label: "Fixture Background" } },
    classes: [{ id: "membership.fixture", name: "Fixture membership",
      class: { id: "class.fixture", label: "Fixture Class" }, level: 1, subclass: null }],
    inventory: { items: [], contentsDepth: 4, mayOmitDeeperContents: true },
    wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
  };
  const calls = [];
  const result = await readCanonicalCharacterSheet({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "fixture",
    actorId: "actor.fixture", perspective: "player",
    fetchImpl: async (input) => {
      calls.push(new URL(input));
      return response(200, { applicationId: "dnd2024", stateSpaceId: "fixture",
        qualifiedQueryId: characterSheetContract.id, outputSchemaHash: characterSheetContract.outputSchemaHash,
        stateSpaceFingerprint: "A".repeat(64), resolutionFingerprint: "B".repeat(64),
        resultFingerprint: "C".repeat(64), sourceRevisionFingerprint: "D".repeat(64), data: sheet });
    },
  });
  assert.equal(result.status, "ready");
  assert.equal(result.data.subject.id, "actor.fixture");
  assert.equal(calls.length, 1);
  assert.match(calls[0].pathname, /\/entities\/actor\.fixture\/read-models\/dnd2024\.query\.character-sheet-v2$/u);
  assert.equal(calls[0].searchParams.get("perspective"), "player");
});

test("canonical senses use named references and reject legacy or partial measurements before rendering", async () => {
  const namedSense = { sense: { id: "dnd2024.vocabulary.sense.darkvision", label: "Darkvision" },
    numerator: 60, denominator: 1, unit: { id: "dnd2024.vocabulary.distance-unit.foot", label: "Foot" } };
  const cases = [
    { sense: namedSense, ready: true },
    { sense: { sense: namedSense.sense }, ready: true },
    { sense: { id: namedSense.sense.id, numerator: 60, denominator: 1, unitId: namedSense.unit.id }, ready: false },
    { sense: { ...namedSense, unit: undefined }, ready: false },
    { sense: { ...namedSense, denominator: 0 }, ready: false },
    { sense: { ...namedSense, sense: { id: namedSense.sense.id } }, ready: false },
    { sense: null, ready: false },
  ];
  for (const item of cases) {
    const data = characterDossier({
      version: 2, subject: { id: "actor.fixture", label: "Fixture Hero" },
      origin: { species: { id: "species.fixture", label: "Fixture Species" }, background: { id: "background.fixture", label: "Fixture Background" } },
      classes: [{ id: "membership.fixture", name: "Fixture membership", class: { id: "class.fixture", label: "Fixture Class" }, level: 1, subclass: null }],
      senses: [item.sense], inventory: { items: [], contentsDepth: 4, mayOmitDeeperContents: true },
      wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
    });
    const result = await readCanonicalCharacter({
      origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "fixture", actorId: "actor.fixture",
      fetchImpl: async () => response(200, { applicationId: "dnd2024", stateSpaceId: "fixture",
        qualifiedQueryId: characterDossierContract.id, outputSchemaHash: characterDossierContract.outputSchemaHash,
        stateSpaceFingerprint: "A".repeat(64), resolutionFingerprint: "B".repeat(64),
        resultFingerprint: "C".repeat(64), sourceRevisionFingerprint: "D".repeat(64), data }),
    });
    assert.equal(result.status, item.ready ? "ready" : "error", JSON.stringify(item.sense));
    if (item.ready) assert.deepEqual(result.data.senses, [item.sense]);
    else {
      assert.equal(result.failureCategory, "incompatible-data");
      assert.equal(result.data, null);
    }
  }
});

function mediaRecord(...attachments) {
  return {
    applicationId: "dnd2024",
    stateSpaceId: "dnd2024-main",
    entityId: "owner",
    resolutionFingerprint: "fixture",
    attachments,
  };
}

test("visual media consumes only owner-authorized discovery and returns no private blob metadata", () => {
  const record = mediaRecord(mediaAttachment("portrait", "Player portrait"));
  assert.deepEqual(projectMediaVisual(record), {
    portrait: {
      imageUrl: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/owner/media/visual-0/content",
      alt: "Player portrait",
      width: 1024,
      height: 1536,
    },
  });
  const serialized = JSON.stringify(projectMediaVisual(record));
  assert.equal(serialized.includes(MEDIA_HASH), false);
  assert.equal(serialized.includes("provenance"), false);
});

test("item media inherits definition roles while explicit instance roles win", () => {
  const definitionIcon = {
    imageUrl: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/definition/media/icon/content",
    alt: "Definition icon", width: 64, height: 64,
  };
  const definitionIllustration = {
    imageUrl: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/definition/media/illustration/content",
    alt: "Definition illustration", width: 600, height: 800,
  };
  const instanceIcon = {
    imageUrl: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/instance/media/icon/content",
    alt: "Instance icon", width: 64, height: 64,
  };
  const inherited = inheritMediaVisual(
    { icon: instanceIcon, gallery: [{ ...instanceIcon, mediaId: "visual-0", role: "icon", caption: "" }] },
    {
      icon: definitionIcon,
      illustration: definitionIllustration,
      gallery: [
        { ...definitionIcon, mediaId: "visual-0", role: "icon", caption: "" },
        { ...definitionIllustration, mediaId: "visual-1", role: "illustration", caption: "" },
      ],
    },
  );

  assert.deepEqual(inherited.icon, instanceIcon);
  assert.deepEqual(inherited.illustration, definitionIllustration);
  assert.deepEqual(inherited.gallery.map((entry) => [entry.role, entry.alt]), [
    ["icon", "Instance icon"],
    ["illustration", "Definition illustration"],
  ]);
});

test("visual media fails closed on malformed or non-owner-bound discovery", () => {
  assert.equal(projectMediaVisual(mediaRecord()), null);
  assert.equal(projectMediaVisual(mediaRecord({ ...mediaAttachment(), contentUrl: "/components/media/file.png" })), null);
  assert.equal(projectMediaVisual(mediaRecord({ ...mediaAttachment(), mediaType: "image/svg+xml" })), null);
  assert.equal(projectMediaVisual({ attachments: [{ ...mediaAttachment(), injected: true }] }), null);
});

test("visual media preserves an ordered gallery when an entity has several authorized images", () => {
  const second = { ...mediaAttachment("scene", "Night at the market", "visual-1"), order: 2 };
  const first = { ...mediaAttachment("setting", "The market at dawn", "visual-0"), order: 1 };
  const projected = projectMediaVisual(mediaRecord(second, first));

  assert.deepEqual(projected.gallery.map((entry) => [entry.mediaId, entry.role, entry.alt]), [
    ["visual-0", "setting", "The market at dawn"],
    ["visual-1", "scene", "Night at the market"],
  ]);
});

test("recorded play situations preserve continuity without becoming authoritative ECS scenes", () => {
  const locationId = "location.thalorien.brackenford";
  assert.deepEqual(resolveRecordedPlaySituation({
    recentMessages: [
      { id: "play-message.1", ordinal: 1, role: "player", text: "I ask about the road." },
      { id: "play-message.2", ordinal: 2, role: "assistant", text: "Tibb answers word for word." },
    ],
    currentSituation: {
      id: "play-situation.1",
      status: "active",
      kind: "conversation",
      summary: "Orban asks Tibb about the closed northern road.",
      participants: [
        { name: "Orban", entityId: "actor.thalorien.brackenford.orban" },
        { name: "Tibb Fallow", entityId: null },
      ],
      location: { name: "Brackenford", entityId: locationId },
    },
  }, [locationId]), {
    status: "ready",
    kind: "recorded",
    locationId,
    recorded: {
      id: "play-situation.1",
      kind: "conversation",
      summary: "Orban asks Tibb about the closed northern road.",
      participants: [
        { id: "actor.thalorien.brackenford.orban", name: "Orban", entityId: "actor.thalorien.brackenford.orban" },
        { id: "play-situation.1.participant.2", name: "Tibb Fallow" },
      ],
      interactions: [
        { id: "play-message.1", ordinal: 1, role: "player", text: "I ask about the road." },
        { id: "play-message.2", ordinal: 2, role: "assistant", text: "Tibb answers word for word." },
      ],
      location: { id: locationId, name: "Brackenford" },
    },
  });
  assert.equal(resolveRecordedPlaySituation({
    recentMessages: [],
    currentSituation: { id: "play-situation.2", status: "active", kind: "initiative", summary: "No.", participants: [] },
  }, []), null);
});

test("known ways onward require admitted exact route and destination subjects", async () => {
  const routeId = "route.thalorien.brackenford-to-crownmere";
  const originId = "location.thalorien.brackenford";
  const destinationId = "location.thalorien.crownmere";
  const requestedKinds = [];
  const routes = await readKnownOpenRoutes({
    fetchImpl: async (input) => {
      const requested = new URL(input);
      const path = requested.pathname;
      if (path.endsWith(`/${routeId}/components/game.core.world.route`)) {
        requestedKinds.push("game.core.world.route");
        return response(200, {
          entityId: routeId,
          qualifiedTypeId: "game.core.world.route",
          valueJson: JSON.stringify({
            status: "active",
            summary: "CANARY GM ROUTE SUMMARY",
            visibility: "gm",
            mode: "on-foot",
            durationMinutes: 45,
          }),
        });
      }
      if (path.endsWith(`/${routeId}/components/game.core.world.route.availability`)) {
        requestedKinds.push("game.core.world.route.availability");
        return response(200, {
          entityId: routeId,
          qualifiedTypeId: "game.core.world.route.availability",
          valueJson: JSON.stringify({ status: "open" }),
        });
      }
      if (path.endsWith(`/${destinationId}/components/game.core.world.location`)) {
        return response(200, {
          entityId: destinationId,
          qualifiedTypeId: "game.core.world.location",
          valueJson: JSON.stringify({
            kind: "settlement", status: "active", summary: "A known port.", visibility: "public",
          }),
        });
      }
      if (path.endsWith("/relationships")) {
        const kind = requested.searchParams.get("qualifiedKind");
        requestedKinds.push(kind);
        const targets = {
          "game.core.world.route.in-world": "world.thalorien",
          "game.core.world.route.from": originId,
          "game.core.world.route.to": destinationId,
        };
        return response(200, { items: [{ fromEntityId: routeId, toEntityId: targets[kind], qualifiedKind: kind }] });
      }
      return response(404, {});
    },
    origin: "http://localhost:6217",
    entityRoot: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities",
    worldId: "world.thalorien",
    currentLocationId: originId,
    perspective: "player",
    projectedKnowledge: {
      status: "ready",
      entries: [
        { text: "The Crownmere road is open.", stance: "known", presentationKind: "statement",
          subject: { id: routeId, name: "Crownmere road" } },
        { text: "Crownmere is a known port.", stance: "known", presentationKind: "statement",
          subject: { id: destinationId, name: "Crownmere" } },
      ],
      locations: [],
    },
    locationDirectory: [{ id: originId, name: "Brackenford" }, { id: destinationId, name: "Crownmere" }],
  });

  assert.deepEqual(routes, [{
    id: routeId,
    originId,
    destinationId,
    destinationName: "Crownmere",
    detail: "The Crownmere road is open.",
    mode: "on-foot",
    durationMinutes: 45,
  }]);
  assert.equal(JSON.stringify(routes).includes("CANARY"), false);
  assert.equal(requestedKinds.every((kind) => kind.startsWith("game.core.")), true);
});

test("known ways onward fail closed without destination knowledge", async () => {
  const routeId = "route.thalorien.brackenford-to-crownmere";
  const originId = "location.thalorien.brackenford";
  const destinationId = "location.thalorien.crownmere";
  const routes = await readKnownOpenRoutes({
    fetchImpl: async (input) => {
      const requested = new URL(input);
      const path = requested.pathname;
      if (path.endsWith(`/${routeId}/components/game.core.world.route`)) {
        return response(200, {
          entityId: routeId,
          qualifiedTypeId: "game.core.world.route",
          valueJson: JSON.stringify({
            status: "active", summary: "A road.", visibility: "public", mode: "on-foot", durationMinutes: 45,
          }),
        });
      }
      if (path.endsWith(`/${routeId}/components/game.core.world.route.availability`)) {
        return response(200, {
          entityId: routeId,
          qualifiedTypeId: "game.core.world.route.availability",
          valueJson: JSON.stringify({ status: "open" }),
        });
      }
      if (path.endsWith("/relationships")) {
        const kind = requested.searchParams.get("qualifiedKind");
        const targets = {
          "game.core.world.route.in-world": "world.thalorien",
          "game.core.world.route.from": originId,
          "game.core.world.route.to": destinationId,
        };
        return response(200, { items: [{ fromEntityId: routeId, toEntityId: targets[kind], qualifiedKind: kind }] });
      }
      return response(404, {});
    },
    origin: "http://localhost:6217",
    entityRoot: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities",
    worldId: "world.thalorien",
    currentLocationId: originId,
    perspective: "player",
    projectedKnowledge: {
      status: "ready",
      entries: [{
        text: "The road is known.", stance: "known", presentationKind: "statement",
        subject: { id: routeId, name: "Road" },
      }],
      locations: [],
    },
    locationDirectory: [{ id: originId, name: "Brackenford" }, { id: destinationId, name: "Crownmere" }],
  });
  assert.deepEqual(routes, []);
});

test("conversation current scene excludes unapproved participants and summary from Player", async () => {
  const entityRoot = "/api/applications/dnd2024/state-spaces/dnd2024-main/entities";
  const calls = [];
  const value = await readConversationCurrentScene({
    fetchImpl: async (input) => {
      const requested = new URL(input);
      calls.push(requested);
      if (requested.pathname.endsWith("/media-batch")) {
        return response(200, {
          applicationId: "dnd2024", stateSpaceId: "dnd2024-main", items: [],
        });
      }
      if (requested.pathname.endsWith("/interaction.brackenford.parley")) {
        return response(200, { entityId: "interaction.brackenford.parley", name: "Gatehouse parley" });
      }
      if (requested.pathname.endsWith("/components/game.core.world.interaction")) {
        return response(200, {
          entityId: "interaction.brackenford.parley",
          qualifiedTypeId: "game.core.world.interaction",
          valueJson: JSON.stringify({
            kind: "conversation",
            status: "accepted",
            summary: "CANARY DM CONVERSATION SUMMARY",
          }),
        });
      }
      if (requested.pathname.endsWith("/relationships")) {
        return response(200, { items: [
          {
            fromEntityId: "interaction.brackenford.parley",
            toEntityId: "actor.hero",
            qualifiedKind: "game.core.world.interaction.participant",
          },
          {
            fromEntityId: "interaction.brackenford.parley",
            toEntityId: "actor.secret-npc",
            qualifiedKind: "game.core.world.interaction.participant",
          },
        ] });
      }
      if (requested.pathname.endsWith("/actor.hero")) {
        return response(200, { entityId: "actor.hero", name: "Hero" });
      }
      throw new Error(`Unexpected request ${requested}`);
    },
    origin: "http://localhost:6217",
    entityRoot,
    conversationId: "interaction.brackenford.parley",
    perspective: "player",
    authorizedActorIds: new Set(["actor.hero"]),
  });
  assert.deepEqual(value, {
    status: "ready",
    kind: "conversation",
    conversation: {
      id: "interaction.brackenford.parley",
      name: "Gatehouse parley",
      participants: [{ id: "actor.hero", name: "Hero" }],
    },
  });
  assert.equal(JSON.stringify(value).includes("CANARY"), false);
  assert.equal(JSON.stringify(value).includes("secret-npc"), false);
  assert.equal(calls.filter((call) => call.pathname.endsWith("/media-batch")).length, 1);
  assert.equal(calls.some((call) => call.pathname.includes("/media/")), false);
});

test("combat current scene uses the Encounter Board's exact participants and active turn", async () => {
  const entityRoot = "/api/applications/dnd2024/state-spaces/dnd2024-main/entities";
  const encounterId = "encounter.brackenford.ambush";
  const participationId = "participation.brackenford.hero";
  const envelope = boardEnvelope();
  envelope.data.participants[0].activeTurn = true;
  envelope.data.turn = {
    id: "turn.brackenford.hero", participationId, ordinal: 0,
  };
  const calls = [];
  const value = await readCombatCurrentScene({
    fetchImpl: async (input) => {
      const requested = new URL(input);
      calls.push(requested);
      if (requested.pathname.endsWith(`/entities/${encounterId}/read-models/dnd2024.query.encounter-board`)) {
        assert.equal(requested.searchParams.get("perspective"), "player");
        return response(200, envelope);
      }
      throw new Error(`Unexpected request ${requested}`);
    },
    origin: "http://localhost:6217",
    entityRoot,
    encounterId,
    stateSpaceId: "dnd2024-main",
    perspective: "player",
    campaignId: "campaign.thalorien",
  });
  assert.deepEqual(value, {
    status: "ready",
    kind: "combat",
    combat: {
      id: encounterId,
      name: "Brackenford ambush",
      participants: [{ id: participationId, name: "Hero", initiative: 17, active: true }],
      board: {
        revision: 7,
        columns: 12,
        rows: 8,
        feetPerSquare: 5,
        terrain: [{ id: "terrain.rubble", label: "Rubble", area: { x: 4, y: 2, width: 2, height: 1 }, movementCost: 2 }],
        obstacles: [{ id: "obstacle.wall", label: "Wall", area: { x: 6, y: 1, width: 1, height: 3 } }],
        participants: [{
          id: participationId,
          name: "Hero",
          initiative: 17,
          active: true,
          position: { x: 2, y: 3, width: 2, height: 1, elevationFeet: 5, revision: 4 },
        }],
        turn: {
          id: "turn.brackenford.hero", participationId, actorName: "Hero", ordinal: 0,
        },
      },
      turn: {
        id: "turn.brackenford.hero", participationId, actorName: "Hero", ordinal: 0,
      },
    },
  });
  assert.equal(calls.length, 1);
  assert.equal(calls.some((call) => call.pathname.includes("/components/")), false);
  assert.equal(calls.some((call) => call.pathname.endsWith("/relationships")), false);
});

function response(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

test("canonical character reads distinguish stale fingerprints, HTTP, authorization, transport, and incompatible data", async () => {
  const common = {
    origin: "http://localhost:6217",
    applicationId: "dnd2024",
    stateSpaceId: "dnd2024-main",
    actorId: "actor.test.hero",
  };
  const serverError = await readCanonicalCharacter({
    ...common,
    fetchImpl: async () => new Response("{}", {
      status: 500,
      headers: { "x-request-id": "request-500" },
    }),
  });
  assert.deepEqual(serverError, {
    status: "error",
    data: null,
    failureCategory: "http",
    diagnosticId: "request-500",
    httpStatus: 500,
  });

  const forbidden = await readCanonicalCharacter({
    ...common,
    fetchImpl: async () => response(403, {}),
  });
  assert.equal(forbidden.status, "forbidden");
  assert.equal(forbidden.failureCategory, "authorization");

  const stale = await readCanonicalCharacter({
    ...common,
    fetchImpl: async () => response(409, {
      code: "READ_MODEL_STATE_SPACE_STALE",
      message: "The state space is not bound to the current application resolution.",
    }),
  });
  assert.equal(stale.status, "error");
  assert.equal(stale.failureCategory, "stale-data");
  assert.equal(stale.errorCode, "READ_MODEL_STATE_SPACE_STALE");
  assert.equal(stale.httpStatus, 409);

  const incompatible = await readCanonicalCharacter({
    ...common,
    fetchImpl: async () => response(200, { data: { version: 1 } }),
  });
  assert.equal(incompatible.status, "error");
  assert.equal(incompatible.failureCategory, "incompatible-data");
  assert.equal(incompatible.data, null);

  const transport = await readCanonicalCharacter({
    ...common,
    fetchImpl: async () => { throw new Error("offline"); },
  });
  assert.equal(transport.status, "error");
  assert.equal(transport.failureCategory, "transport");
});

test("normalizes only a credential-free HTTP(S) server origin", () => {
  assert.equal(normalizeGameServerOrigin("http://localhost:6217"), "http://localhost:6217");
  assert.equal(normalizeGameServerOrigin("https://table.example.test/"), "https://table.example.test");
  assert.equal(normalizeGameServerOrigin("http://user@example.test"), null);
  assert.equal(normalizeGameServerOrigin("http://example.test/api"), null);
  assert.equal(normalizeGameServerOrigin("file:///campaign"), null);
});

test("does not read campaign state after an audience denial", async () => {
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    fetchImpl: async () => response(403, { status: "denied", error: "AUDIENCE_CONTEXT_DENIED" }),
  });

  assert.deepEqual(value, {
    version: 1,
    status: "denied",
    message: "The game server did not authorize a campaign for this local table.",
  });
});

test("rejects an actor's cross-campaign request before reading campaign detail", async () => {
  const calls = [];
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    requestedCampaignId: "campaign.embersea.black-tide",
    fetchImpl: async (input) => {
      const path = new URL(input).pathname;
      calls.push(path);
      if (path === "/api/audience-context") {
        return response(200, {
          status: "bound",
          applicationId: "dnd2024",
          stateSpaceId: "dnd2024-main",
          campaignId: "campaign.thalorien.brackenford",
          actorId: "actor.thalorien.brackenford.orban",
          role: "actor",
        });
      }
      throw new Error(`Unexpected request ${path}`);
    },
  });

  assert.deepEqual(value, {
    version: 1,
    status: "denied",
    message: "That campaign is not available to this local table.",
  });
  assert.deepEqual(calls, ["/api/audience-context"]);
});

test("registered current play cross-checks exact exploration, conversation, and combat transitions", async () => {
  for (const kind of ["exploration", "conversation", "combat"]) {
    const scene = currentSceneData(kind, [{
      key: "continue", label: "Continue", summary: "Continue from authoritative state.",
    }]);
    const resumeScene = {
      locationId: scene.location.id,
      conversationId: scene.conversationId,
      encounterId: scene.encounterId,
    };
    const calls = [];
    const result = await readRegisteredCurrentPlay({
      origin: "http://localhost:6217", applicationId: "dnd2024",
      stateSpaceId: "dnd2024-main", campaignId: "campaign.thalorien", perspective: "player",
      fetchImpl: async (input) => {
        const requested = new URL(input);
        calls.push(requested);
        const contract = requested.pathname.endsWith(campaignResumeContract.id)
          ? campaignResumeContract
          : currentSceneContract;
        const data = contract === campaignResumeContract
          ? campaignResumeData(resumeScene, scene.affordances)
          : scene;
        return response(200, currentPlayEnvelope(
          contract,
          data,
          contract === campaignResumeContract ? "a".repeat(64) : "b".repeat(64),
        ));
      },
    });
    assert.equal(result.status, "ready");
    assert.equal(result.scene.kind, kind);
    assert.deepEqual(result.scene.affordances, scene.affordances);
    assert.equal(calls.length, 2);
    assert.ok(calls.every((call) => call.searchParams.get("campaignId") === "campaign.thalorien"));
    assert.ok(calls.every((call) => call.searchParams.get("perspective") === "player"));
  }
});

test("registered current play treats a Resume null scene as explicit and skips Current Scene", async () => {
  const calls = [];
  const result = await readRegisteredCurrentPlay({
    origin: "http://localhost:6217", applicationId: "dnd2024",
    stateSpaceId: "dnd2024-main", campaignId: "campaign.thalorien", perspective: "dm",
    fetchImpl: async (input) => {
      calls.push(new URL(input));
      return response(200, currentPlayEnvelope(campaignResumeContract, campaignResumeData(null)));
    },
  });
  assert.equal(result.status, "empty");
  assert.equal(result.resume.scene, null);
  assert.equal(calls.length, 1);
  assert.ok(calls[0].pathname.endsWith(campaignResumeContract.id));
});

test("registered current play rejects incoherent cross-query state and preserves Player denial", async () => {
  const scene = currentSceneData("combat");
  const resume = campaignResumeData({
    locationId: "location.thalorien.somewhere-else", conversationId: null, encounterId: scene.encounterId,
  });
  const stale = await readRegisteredCurrentPlay({
    origin: "http://localhost:6217", applicationId: "dnd2024",
    stateSpaceId: "dnd2024-main", campaignId: "campaign.thalorien", perspective: "player",
    fetchImpl: async (input) => new URL(input).pathname.endsWith(campaignResumeContract.id)
      ? response(200, currentPlayEnvelope(campaignResumeContract, resume))
      : response(200, currentPlayEnvelope(currentSceneContract, scene)),
  });
  assert.equal(stale.status, "stale");

  const denied = await readRegisteredCurrentPlay({
    origin: "http://localhost:6217", applicationId: "dnd2024",
    stateSpaceId: "dnd2024-main", campaignId: "campaign.thalorien", perspective: "player",
    fetchImpl: async () => response(403, {}),
  });
  assert.deepEqual(denied, { status: "forbidden" });
});
