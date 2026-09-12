import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { boardEnvelope } from "./fixtures/encounter-board.js";
import { connectedCampaignToHubEnvelope } from "../src/server/connected-hub-envelope.ts";
import { projectRegisteredPartyReferences } from "../src/server/campaign-summary.js";
import { resolveHubSurface } from "../src/data/hub-availability.js";
import { contract as campaignSummaryContract } from "../src/server/campaign-summary-contract.js";
import { contract as campaignContextContract } from "../src/server/campaign-context-contract.js";
import { contract as campaignDetailsContract } from "../src/server/campaign-details-contract.js";
import { contract as characterSheetContract } from "../src/server/character-sheet-contract.js";
import { contract as characterDossierContract } from "../src/server/character-dossier-contract.js";
import { contract as factionDirectoryContract } from "../src/server/faction-directory-contract.js";
import { contract as inventoryContainerContract } from "../src/server/inventory-container-contract.js";
import { contract as inventoryWalletContract } from "../src/server/inventory-wallet-contract.js";
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
  readCanonicalInventoryPage,
  readConversationCurrentScene,
  readGameServerContext,
  readRegisteredCampaignSummary,
  readRegisteredCampaignDetails,
  readRegisteredCurrentPlay,
  readRegisteredFactionDirectoryPage,
  readRegisteredWorldLocationScope,
  readRegisteredWorldLocationScopePage,
  readWorldLocationScopePatch,
  readWorldLocationDirectory,
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
        }, { entityId: "place-azure", attachments: [] }],
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

test("World and Locations safely probes a child when known-empty evidence is absent", async () => {
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
      const target = new URL(input);
      calls.push(target);
      if (target.pathname.includes("/entities/place-azure/")) return response(200, worldScopePageEnvelope({
        version: 1, state: "ready",
        scope: {
          id: "place-azure", name: "Azure Reach", parentId: "realm-root-7", slot: "region",
          kind: "region", status: "active", summary: "The coast beneath blue cliffs.",
          visibility: "public", mapAnchor: { x: 125, y: 875 },
        },
        locations: [], totalCount: 0, complete: true, nextCursor: null,
      }));
      return response(200, worldScopePageEnvelope(worldLocationScopePageData()));
    },
  });
  assert.deepEqual(new Set(patch.locationDirectory.map((item) => item.id)), new Set(["realm-root-7", "place-azure"]));
  assert.deepEqual(patch.locationScopes.find((scope) => scope.id === "realm-root-7"), {
    id: "realm-root-7", name: "The Seventh Realm", parentId: null,
    childIds: ["place-azure"], totalCount: 1, complete: true, nextCursor: null,
    sourceRevisionFingerprint: "4".repeat(64),
  });
  assert.equal(patch.locationDirectoryAudience, "player");
  assert.equal(patch.locationDirectoryComplete, true);
  assert.deepEqual(patch.locationScopes.find((scope) => scope.id === "place-azure")?.childIds, []);
  assert.equal(calls.length, 2);
  assert.ok(calls.every((call) => !/\/entities$/u.test(call.pathname) && !call.pathname.endsWith("/media")));
});

test("false or malformed known-empty hints cannot suppress an authorized child-scope probe", async () => {
  for (const childScopeKnownEmpty of [false, "not-authoritative"]) {
    const calls = [];
    const source = {
      applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
      audience: { seat: "dm", perspective: "player", allowedPerspectives: ["dm", "player"] },
      contextSelection: { selectedWorldId: "realm-root-7", selectedCampaignId: "campaign-9", worlds: [] },
    };
    const patch = await readWorldLocationDirectory({
      origin: "http://localhost:6217", source,
      fetchImpl: async (input) => {
        const target = new URL(input);
        calls.push(target);
        if (target.pathname.includes("/entities/place-azure/")) return response(200, worldScopePageEnvelope({
          ...worldLocationScopePageData("place-azure", []),
          scope: { ...worldLocationScopeData().locations[0] },
        }));
        return response(200, worldScopePageEnvelope(worldLocationScopePageData("realm-root-7", [{
          ...worldLocationScopeData().locations[0], childScopeKnownEmpty,
        }])));
      },
    });
    assert.equal(patch.locationDirectoryComplete, true);
    assert.equal(patch.locationScopes.find(({ id }) => id === "place-azure")?.complete, true);
    assert.equal(calls.length, 2, String(childScopeKnownEmpty));
  }
});

test("location directory skips only explicitly known-empty child scopes without loading media", async () => {
  const calls = [];
  const source = {
    applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    contextSelection: { selectedWorldId: "realm-root-7", selectedCampaignId: "campaign-9", worlds: [] },
    locationDirectory: [{
      id: "realm-root-7", name: "The Seventh Realm", kind: "world", isWorldRoot: true,
      summary: "Detailed root summary.",
    }, {
      id: "atlas", name: "Atlas", kind: "region", containerId: "realm-root-7",
    }],
    locationScopes: [{
      id: "realm-root-7", name: "The Seventh Realm", parentId: null, childIds: ["atlas"],
      totalCount: 1, complete: true, nextCursor: null, sourceRevisionFingerprint: "4".repeat(64),
    }],
  };
  const patch = await readWorldLocationDirectory({
    origin: "http://localhost:6217", source,
    fetchImpl: async (input) => {
      const target = new URL(input);
      calls.push(target);
      const scopeId = decodeURIComponent(target.pathname.split("/entities/")[1].split("/read-models/")[0]);
      const locations = scopeId === "atlas" ? [{
        id: "juniper", name: "Juniper Gate", parentId: "atlas", slot: "settlement",
        kind: "settlement", status: "active", summary: "A guarded gate.", visibility: "public",
        mapAnchor: { x: 500, y: 500 }, childScopeKnownEmpty: true,
      }] : [];
      const scope = scopeId === "atlas" ? {
        id: "atlas", name: "Atlas", parentId: "realm-root-7", slot: "region", kind: "region",
        status: "active", summary: "A mapped region.", visibility: "public", mapAnchor: null,
      } : {
        id: "juniper", name: "Juniper Gate", parentId: "atlas", slot: "settlement",
        kind: "settlement", status: "active", summary: "A guarded gate.", visibility: "public",
        mapAnchor: { x: 500, y: 500 },
      };
      return response(200, worldScopePageEnvelope({
        version: 1, state: "ready", scope, locations, totalCount: locations.length,
        complete: true, nextCursor: null,
      }));
    },
  });

  assert.equal(patch.locationDirectoryComplete, true);
  assert.deepEqual(patch.locationDirectory.map(({ id }) => id), ["atlas", "juniper", "realm-root-7"]);
  assert.deepEqual(patch.locationScopes.find(({ id }) => id === "juniper")?.childIds, []);
  assert.equal(patch.locationDirectory.find(({ id }) => id === "realm-root-7")?.summary,
    "Detailed root summary.");
  assert.equal(calls.length, 1);
  assert.match(calls[0].pathname, /entities\/atlas\/read-models/u);
  assert.ok(calls.every((call) => /dnd2024\.query\.world-location-scope-page$/u.test(call.pathname)));
  assert.ok(calls.every((call) => !call.pathname.endsWith("/media-batch")));
});

function inventoryContainerData(actorId) {
  return {
    version: 2,
    container: { id: actorId, label: "Ganji" },
    state: "ready",
    reasons: [],
    items: [
      {
        id: "inventory.backpack", name: "Backpack", definition: { id: "item.backpack", label: "Backpack" },
        quantity: 1, slot: "carried", order: 0, equipmentSlots: [], classification: "item", isContainer: true,
      },
    ],
    limits: { contentsDepth: 1, itemCount: 200, directComplete: true, recursiveComplete: false },
  };
}

function inventoryWalletData(actorId, complete = true) {
  return {
    version: 1, owner: { id: actorId, label: "Ganji" }, state: complete ? "ready" : "partial",
    reasons: complete ? [] : ["depth-limit"],
    wallet: { coinCount: 3, copperValue: 300, gpCount: 3, denominations: [
      { denomination: { id: "currency.gp", label: "Gold piece" }, code: "gp", count: 3,
        copperValuePerCoin: 100, totalCopperValue: 300 },
    ] },
    limits: { contentsDepth: 4, complete },
  };
}

test("inventory root reads independent direct contents and wallet without item-tab fan-out", async () => {
  const calls = [];
  const actorId = "actor.ganji";
  const result = await readCanonicalInventory({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    actorId, perspective: "player",
    fetchImpl: async (input) => {
      const request = new URL(input); calls.push(request);
      const wallet = request.pathname.endsWith(inventoryWalletContract.id);
      return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
        qualifiedQueryId: wallet ? inventoryWalletContract.id : inventoryContainerContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: wallet ? inventoryWalletContract.outputSchemaHash : inventoryContainerContract.outputSchemaHash,
        resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
        data: wallet ? inventoryWalletData(actorId) : inventoryContainerData(actorId),
      });
    },
  });
  assert.equal(result.status, "ready");
  assert.deepEqual(result.data.items.map((item) => [item.id, item.parentItemId]), [["inventory.backpack", null]]);
  assert.equal(result.data.walletState.status, "complete");
  assert.equal(calls.length, 2);
  assert.ok(calls.every((call) => call.searchParams.get("perspective") === "player"));
  assert.ok(calls.some((call) => call.pathname.endsWith(inventoryContainerContract.id)));
  assert.ok(calls.some((call) => call.pathname.endsWith(inventoryWalletContract.id)));
  assert.ok(calls.every((call) => !/recipes|uses|character-dossier/.test(call.pathname)));

  const walletUnavailable = await readCanonicalInventory({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    actorId, perspective: "dm",
    fetchImpl: async (input) => {
      const request = new URL(input);
      if (request.pathname.endsWith(inventoryWalletContract.id)) return response(500, { code: "wallet-failed" });
      return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
        qualifiedQueryId: inventoryContainerContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: inventoryContainerContract.outputSchemaHash, resultFingerprint: "3".repeat(64),
        sourceRevisionFingerprint: "4".repeat(64), data: inventoryContainerData(actorId),
      });
    },
  });
  assert.equal(walletUnavailable.status, "ready");
  assert.equal(walletUnavailable.data.wallet, null);
  assert.deepEqual(walletUnavailable.data.walletState, { status: "unavailable", reason: "read-failed" });
});

test("location reads consume fields across domain versions and localize malformed descriptive fields", async () => {
  for (const paged of [false, true]) {
    const data = paged ? worldLocationScopePageData() : worldLocationScopeData();
    data.version = 999;
    data.extra = { unrelated: true };
    data.scope.name = { incompatible: true };
    data.scope.extra = "ignored";
    data.locations[0].summary = ["not a description"];
    data.locations[0].mapAnchor = { x: -1, y: 800 };
    const read = paged ? readRegisteredWorldLocationScopePage : readRegisteredWorldLocationScope;
    const result = await read({
      origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
      scopeId: "realm-root-7", perspective: "player", includeMedia: false,
      fetchImpl: async () => response(200, paged ? worldScopePageEnvelope(data) : worldScopeEnvelope(data)),
    });
    assert.equal(result.status, "ready");
    assert.equal(result.coverage, "partial");
    const scope = paged ? result.scope : result.items[0];
    const child = paged ? result.items[0] : result.items[1];
    assert.equal(scope.name, "Name unavailable");
    assert.equal(child.name, "Azure Reach");
    assert.equal(child.kind, "region");
    assert.equal(child.summary, undefined);
    assert.equal(child.mapAnchor, undefined, "no invented zero coordinate");
    assert.deepEqual(child.unavailableFields, ["summary", "mapAnchor"]);
    assert.equal(JSON.stringify(result).includes("unrelated"), false);
  }
});

test("location collection identity failures omit ambiguous rows with explicit partial coverage", async () => {
  const data = worldLocationScopePageData();
  data.locations.push({ ...data.locations[0] }, null,
    { ...data.locations[0], id: "place-readable", name: "Readable place" });
  data.totalCount = data.locations.length;
  const source = { applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    audience: { seat: "dm", perspective: "dm" }, contextSelection: { selectedWorldId: "realm-root-7" } };
  const result = await readWorldLocationScopePatch({ source, scopeId: "realm-root-7",
    origin: "http://localhost:6217", fetchImpl: async (input) =>
      new URL(input).pathname.endsWith("/media-batch") ? response(503, {}) : response(200, worldScopePageEnvelope(data)),
  });
  assert.deepEqual(result.locationScopes[0].childIds, ["place-readable"]);
  assert.equal(result.locationScopes[0].coverage, "partial");
  assert.equal(result.locationScopes[0].complete, true, "paging completion does not imply display completeness");
  assert.equal(result.locationScopes[0].totalCount, 4);
});

test("field-local locations still reject foreign scope, containment and invalid paging", async () => {
  for (const mutate of [
    (data) => { data.scope.id = "another-world"; },
    (data) => { data.locations[0].parentId = "another-world"; },
    (data) => { data.locations[0].id = data.scope.id; },
    (data) => { data.nextCursor = "arbitrary"; },
    (data) => { data.totalCount = 200; },
    (data) => { data.state = "unrecognized"; },
  ]) {
    const data = worldLocationScopePageData();
    mutate(data);
    const result = await readRegisteredWorldLocationScopePage({
      origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
      scopeId: "realm-root-7", perspective: "player", includeMedia: false,
      fetchImpl: async () => response(200, worldScopePageEnvelope(data)),
    });
    assert.equal(result.status, "error");
    assert.deepEqual(result.items, []);
  }
});

test("inventory rows survive wallet denomination fields that are not disclosed", async () => {
  const actorId = "actor.ganji";
  const result = await readCanonicalInventory({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    actorId, perspective: "dm",
    fetchImpl: async (input) => {
      const request = new URL(input);
      if (request.pathname.endsWith(inventoryWalletContract.id)) return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
        qualifiedQueryId: inventoryWalletContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: "a".repeat(64), resultFingerprint: "3".repeat(64),
        sourceRevisionFingerprint: "4".repeat(64),
        data: {
          version: 1, owner: { id: actorId, label: "Ganji" }, state: "ready", reasons: [],
          wallet: {
            coinCount: 3, copperValue: 300, gpCount: 3,
            denominations: [{ denomination: { id: "currency.gp", label: "Gold piece" }, code: "gp", count: 3 }],
          },
          limits: { contentsDepth: 4, complete: true },
        },
      });
      return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
        qualifiedQueryId: inventoryContainerContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: "b".repeat(64), resultFingerprint: "3".repeat(64),
        sourceRevisionFingerprint: "4".repeat(64), data: inventoryContainerData(actorId),
      });
    },
  });
  assert.equal(result.status, "ready");
  assert.equal(result.data.items[0].name, "Backpack");
  assert.deepEqual(result.data.wallet, { coinCount: 3, copperValue: 300, gpCount: 3,
    denominations: [{ denomination: { id: "currency.gp", label: "Gold piece" }, code: "gp", count: 3 }] });
  assert.deepEqual(result.data.walletState, { status: "complete", reason: null });
});

test("scoped inventory pages reject cycles and preserve authorization failures", async () => {
  const actorId = "actor.ganji";
  const cycle = inventoryContainerData(actorId);
  cycle.items[0].id = actorId;
  const incompatible = await readCanonicalInventoryPage({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    scopeId: actorId, perspective: "dm", fetchImpl: async () => response(200, {
      applicationId: "dnd2024", stateSpaceId: "dnd2024-main", qualifiedQueryId: inventoryContainerContract.id,
      stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
      outputSchemaHash: inventoryContainerContract.outputSchemaHash, resultFingerprint: "3".repeat(64),
      sourceRevisionFingerprint: "4".repeat(64), data: cycle,
    }),
  });
  assert.equal(incompatible.status, "ready");
  assert.deepEqual(incompatible.data.items, []);
  assert.equal(incompatible.data.state, "partial");
  assert.ok(incompatible.data.notices.includes("invalid-item-identity"));

  const forbidden = await readCanonicalInventoryPage({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    scopeId: actorId, perspective: "player", fetchImpl: async () => response(403, { code: "forbidden" }),
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

test("Campaign summary keeps authorized roster and paging when descriptive fields are unavailable", async () => {
  const campaignId = "campaign.caldris.measure-of-mercy";
  const load = (data, perspective = "player") => readRegisteredCampaignSummary({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    campaignId, perspective,
    fetchImpl: async () => response(200, {
      applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
      qualifiedQueryId: campaignSummaryContract.id,
      stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
      outputSchemaHash: campaignSummaryContract.outputSchemaHash, resultFingerprint: "3".repeat(64),
      sourceRevisionFingerprint: "4".repeat(64), data,
    }),
  });
  const source = {
    status: "active", title: 7, premise: { unexpected: true }, partyGoals: ["Keep going.", 4],
    toneAndBoundaries: null, unrelatedMetadata: { future: true },
    party: [{ id: "participation.ganji", name: "Ganji participation", status: "active", future: true }],
    totalCount: 1, complete: true, nextCursor: null,
  };
  const summary = await load(source);
  assert.equal(summary.title, null);
  assert.equal(summary.premise, null);
  assert.deepEqual(summary.partyGoals, ["Keep going."]);
  assert.deepEqual(summary.toneAndBoundaries, []);
  assert.deepEqual(summary.descriptiveFields, {
    title: "invalid", premise: "invalid", partyGoals: "partial", toneAndBoundaries: "empty",
  });
  assert.deepEqual(summary.party, [{ id: "participation.ganji", name: "Ganji participation", status: "active" }]);

  const incomplete = await load({ ...source, complete: false, nextCursor: "next-page" });
  assert.equal(incomplete.complete, false);
  assert.equal(incomplete.nextCursor, "next-page");
  assert.equal(await load({ ...source, party: [{
    id: "participation.ganji", name: "Ganji participation", status: "active",
    actors: [{ id: "actor.ganji", name: "Ganji" }],
  }] }), null);
  const dmSummary = await load({ ...source, party: [{
    id: "participation.ganji", status: "active", participationMetadata: "future",
    actors: [{ id: "actor.ganji", actorMetadata: "future" }],
  }] }, "dm");
  assert.deepEqual(dmSummary.party, [{
    id: "participation.ganji", name: "participation.ganji", status: "active",
    actors: [{ id: "actor.ganji", name: "actor.ganji" }],
  }]);
  assert.deepEqual(projectRegisteredPartyReferences(dmSummary.party), [{
    id: "actor.ganji", name: "actor.ganji", state: "active", current: false, entries: [], detailsDeferred: true,
  }]);
});

test("Campaign bootstrap retains its independently bound campaign identity when summary descriptions fail", async () => {
  const { value } = await readRegisteredPartyBootstrap({
    summary: {
      title: 7, premise: { unexpected: true }, partyGoals: "not-a-list", toneAndBoundaries: null,
      futureMetadata: true,
    },
  });
  assert.equal(value.status, "connected");
  assert.equal(value.campaign.name, "The Measure of Mercy");
  assert.deepEqual(value.campaign.descriptiveFields, {
    title: "invalid", premise: "invalid", partyGoals: "invalid", toneAndBoundaries: "empty",
  });
  const hub = connectedCampaignToHubEnvelope(value);
  assert.equal(hub.campaign.premise, "Campaign premise is unavailable.");
  assert.equal(hub.campaign.objective, "Party objectives are unavailable.");
  assert.equal(hub.campaign.facts.find((fact) => fact.label === "Party goals")?.value, "Unavailable");
  assert.equal(hub.campaign.descriptiveFields?.premise, "invalid");

  const partialGoals = await readRegisteredPartyBootstrap({ summary: { partyGoals: ["Protect the party.", 4] } });
  const partialHub = connectedCampaignToHubEnvelope(partialGoals.value);
  assert.equal(partialHub.campaign.descriptiveFields?.partyGoals, "partial");
  assert.equal(partialHub.campaign.objective, "Protect the party.");

  const partial = await readRegisteredPartyBootstrap({ summary: { complete: false, nextCursor: "page-2" } });
  assert.equal(partial.value.status, "unavailable");
});

test("Player Campaign details reject GM fields and session records", async () => {
  const calls = [];
  const load = (data) => readRegisteredCampaignDetails({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    campaignId: "campaign.caldris.measure-of-mercy", perspective: "player",
    fetchImpl: async (input) => {
      calls.push(new URL(input));
      return response(200, {
      applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
      qualifiedQueryId: campaignDetailsContract.id,
      stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
      outputSchemaHash: campaignDetailsContract.outputSchemaHash, resultFingerprint: "3".repeat(64),
      sourceRevisionFingerprint: "4".repeat(64), data,
    });
    },
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
  assert.ok(calls.every((call) => call.searchParams.get("campaignId") === null));
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

const factionPageItem = (overrides = {}) => ({
  id: "faction.caldris.tensor-sect", name: "Tensor Sect", status: "active",
  visibility: "public", summary: "A disciplined order.", goals: ["Perfect perception."],
  methods: ["Study."], assets: [], agenda: { state: "ready", summary: "Retrieve Ganji." },
  members: [{ id: "actor.caldris.ganji", name: "Ganji" }],
  controlledSites: [{ id: "location.caldris.ninth-angle", name: "House of the Ninth Angle" }],
  territories: [{ id: "location.caldris.highmead", name: "Highmead" }],
  allies: [], opponents: [], ...overrides,
});

function factionPageResponse(items, { totalCount = items.length, complete = true, nextCursor = null } = {}) {
  return response(200, {
    applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    qualifiedQueryId: factionDirectoryContract.id,
    stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
    outputSchemaHash: factionDirectoryContract.outputSchemaHash,
    resultFingerprint: "4".repeat(64), sourceRevisionFingerprint: "A".repeat(64),
    data: { worldSummary: "A world shaped by rival powers.", items, totalCount, complete, nextCursor },
  });
}

test("location scope mechanic exposes only a no-children proof without hidden identities", () => {
  const source = readFileSync(new URL(
    "../../../../../catalog/applications/dnd2024/mechanics/world/" +
      "dnd2024.mechanic.world.location-scope.page.js",
    import.meta.url,
  ), "utf8");
  const location = (visibility) => JSON.stringify({
    kind: "region", status: "active", summary: "A bounded place.", visibility,
  });
  const output = Function("ctx", source)({
    audience: { perspective: "player" },
    input: { offset: 0, expectedSourceRevision: null },
    roles: { scope: {
      id: "world.one", name: "World", components: {
        "game.core.world.root": JSON.stringify({
          status: "active", summary: "A bounded world.", visibility: "party",
        }),
      },
      contains: [
        { id: "location.empty", name: "Empty", slot: "region",
          components: { "game.core.world.location": location("public") } },
        { id: "location.branch", name: "Branch", slot: "region", deeperContentsOmitted: true,
          components: { "game.core.world.location": location("public") } },
        { id: "location.hidden", name: "Hidden", slot: "region",
          components: { "game.core.world.location": location("gm") } },
      ],
    } },
  });
  assert.deepEqual(output.data.locations.map(({ id }) => id), ["location.branch", "location.empty"]);
  assert.equal(Object.hasOwn(output.data.locations[0], "childScopeKnownEmpty"), false);
  assert.equal(output.data.locations[1].childScopeKnownEmpty, true);
  assert.doesNotMatch(JSON.stringify(output.data), /location\.hidden|Hidden/u);
});

test("registered faction rows retain safe identities while localizing malformed fields", async () => {
  const page = await readRegisteredFactionDirectoryPage({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    worldId: "world.caldris", fetchImpl: async () => factionPageResponse([factionPageItem({
      name: 42, summary: 42, goals: ["Keep the road open", 42], methods: undefined,
      status: "unfamiliar", visibility: undefined, agenda: { state: "ready", summary: 42 },
      members: [
        { id: "member.known", name: 42 },
        { id: "member.duplicate", name: "First" },
        { id: "member.duplicate", name: "Second" },
      ],
      controlledSites: [{ id: "place.shared", name: "Shared place" }],
      territories: [
        { id: "place.shared", name: "Shared place again" },
        { id: "place.unique", name: 42 },
      ],
    })]),
  });
  assert.equal(page.factions.length, 1);
  const faction = page.factions[0];
  assert.equal(faction.name, "Name unavailable");
  assert.equal(faction.summary, "Summary unavailable.");
  assert.deepEqual(faction.goals, ["Keep the road open"]);
  assert.deepEqual(faction.methods, []);
  assert.deepEqual(faction.memberIds, ["member.known"]);
  assert.deepEqual(faction.memberReferences, [{ id: "member.known", name: "Name unavailable" }]);
  assert.deepEqual(faction.territoryIds, ["place.unique"]);
  assert.deepEqual(faction.territoryReferences, [{ id: "place.unique", name: "Name unavailable" }]);
  assert.ok(faction.unavailableFields.includes("summary"));
  assert.ok(faction.unavailableFields.includes("name"));
  assert.ok(faction.unavailableFields.includes("methods"));
  assert.ok(faction.unavailableFields.includes("members"));
  assert.ok(faction.unavailableFields.includes("territories"));
  assert.ok(faction.unavailableFields.includes("agenda"));
});

test("registered faction pages omit every duplicate identity without claiming confirmed emptiness", async () => {
  const duplicate = factionPageItem();
  const page = await readRegisteredFactionDirectoryPage({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    worldId: "world.caldris", fetchImpl: async () => factionPageResponse([
      duplicate, { ...duplicate, name: "Duplicate display" },
      factionPageItem({ id: "faction.caldris.other", name: "Other faction" }),
    ], { totalCount: 3 }),
  });
  assert.equal(page.factions.length, 1);
  assert.equal(page.factions[0].id, "faction.caldris.other");
  assert.equal(page.totalCount, 3);
  assert.equal(page.complete, true);
  assert.equal(page.coverage, "partial");
});

test("T01 T02 Faction display ignores unrelated page descriptions and excludes unsafe identities locally", async () => {
  const page = await readRegisteredFactionDirectoryPage({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    worldId: "world.caldris", fetchImpl: async () => {
      const value = await factionPageResponse([factionPageItem(), factionPageItem({ id: "__proto__" })]).json();
      value.outputSchemaHash = "F".repeat(64);
      value.data.worldSummary = false;
      value.data.addedMetadata = { ignored: true };
      return response(200, value);
    },
  });
  assert.equal(page.factions.length, 1);
  assert.equal(page.factions[0].id, factionPageItem().id);
  assert.equal(page.coverage, "partial");
  assert.equal(page.totalCount, 2);
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

test("shared website normalizes obsolete Player preference to the full DM table view", async () => {
  const { value, calls } = await readRegisteredPartyBootstrap({ shared: true, perspective: "player" });
  assert.equal(value.status, "connected");
  assert.deepEqual(value.audience, {
    seat: "dm", perspective: "dm", allowedPerspectives: ["dm"], websiteAccess: "shared",
  });
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
    "/entities/campaign.caldris.measure-of-mercy/read-models/dnd2024.query.campaign-context",
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

for (const changedIndex of [0, 19]) {
  test(`batched roster preserves authorized identity with missing display name and extra fields at ${changedIndex + 1}`, async () => {
    const party = Array.from({ length: 20 }, (_, index) => partyReference(index));
    delete party[changedIndex].actors[0].name;
    party[changedIndex].actors[0].secret = "PRIVATE_FIELD_CANARY";
    const { value, calls } = await readRegisteredPartyBootstrap({ party });
    assert.equal(value.status, "connected");
    assert.deepEqual(value.party.map(entry => entry.id), party.flatMap(entry => entry.actors.map(actor => actor.id)));
    assert.equal(value.party[changedIndex].name, party[changedIndex].actors[0].id);
    assert.equal(JSON.stringify(value).includes("PRIVATE_FIELD_CANARY"), false);
    assert.equal(JSON.stringify(connectedCampaignToHubEnvelope(value)).includes("PRIVATE_FIELD_CANARY"), false);
    assert.equal(calls.length, 3);
  });
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

test("batched roster deduplicates shared identities and excludes withdrawn participants regardless of display names", async () => {
  const duplicate = { ...partyReference(1), actors: partyReference(0).actors };
  const withdrawn = { ...partyReference(2), status: "withdrawn", actors: [] };
  const { value, calls } = await readRegisteredPartyBootstrap({ party: [partyReference(0), duplicate, withdrawn] });
  assert.equal(value.status, "connected");
  assert.deepEqual(value.party.map(entry => entry.id), ["actor.0"]);
  assert.equal(calls.length, 3);
  const empty = await readRegisteredPartyBootstrap({ party: [withdrawn] });
  assert.deepEqual(empty.value.party, []);
  duplicate.actors = [{ id: "actor.0", name: "Conflicting name" }];
  const renamed = (await readRegisteredPartyBootstrap({ party: [partyReference(0), duplicate] })).value;
  assert.equal(renamed.status, "connected");
  assert.deepEqual(renamed.party.map(entry => entry.id), ["actor.0"]);
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

test("character resources load the actor portrait independently of inventory and fail closed", async () => {
  const sheet = {
    version: 2, subject: { id: "actor.fixture", label: "Fixture Hero" },
    origin: { species: { id: "species.fixture", label: "Fixture Species" },
      background: { id: "background.fixture", label: "Fixture Background" } },
    classes: [{ id: "membership.fixture", name: "Fixture membership",
      class: { id: "class.fixture", label: "Fixture Class" }, level: 1, subclass: null }],
    inventory: { items: [], contentsDepth: 4, mayOmitDeeperContents: true },
    wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
  };
  for (const [read, contract, data] of [
    [readCanonicalCharacterSheet, characterSheetContract, sheet],
    [readCanonicalCharacter, characterDossierContract, characterDossier(sheet)],
  ]) {
    for (const mode of ["portrait", "missing", "unavailable", "foreign-owner", "unauthorized", "unauthorized-401", "malformed", "player"]) {
      const calls = [];
      const result = await read({
        origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "fixture",
        actorId: "actor.fixture", perspective: mode === "player" ? "player" : "dm",
        fetchImpl: async (input, init) => {
          const path = new URL(input).pathname;
          calls.push(path);
          if (path.endsWith("/media-batch")) {
            assert.deepEqual(JSON.parse(init.body), { entityIds: ["actor.fixture"], perspective: "dm" });
            if (mode === "unavailable") throw new Error("Media unavailable");
            if (mode === "unauthorized" || mode === "unauthorized-401") {
              return response(mode === "unauthorized-401" ? 401 : 403, { code: "media-forbidden" });
            }
            return response(200, { applicationId: "dnd2024", stateSpaceId: "fixture",
              items: mode === "missing" ? [] : [{
                entityId: mode === "foreign-owner" ? "actor.other" : "actor.fixture",
                attachments: mode === "malformed" ? { not: "an-array" } : [mediaAttachment()],
              }] });
          }
          return response(200, { applicationId: "dnd2024", stateSpaceId: "fixture",
            qualifiedQueryId: contract.id, outputSchemaHash: contract.outputSchemaHash,
            stateSpaceFingerprint: "A".repeat(64), resolutionFingerprint: "B".repeat(64),
            resultFingerprint: "C".repeat(64), sourceRevisionFingerprint: "D".repeat(64), data });
        },
      });
      assert.equal(result.status, "ready", `${contract.id}: ${mode}`);
      assert.equal(calls.length, mode === "player" ? 1 : 2);
      if (mode === "portrait") assert.equal(result.media.portrait.alt, "A reviewed portrait");
      else if (mode === "missing" || mode === "unauthorized" || mode === "unauthorized-401" || mode === "player") assert.equal(result.media, null);
      else assert.equal(result.media, undefined, `${contract.id}: ${mode} keeps transport failures unknown`);
    }
  }
});

test("player preview sheet transport failure clears portrait without making a media request", async () => {
  const calls = [];
  const result = await readCanonicalCharacterSheet({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "fixture",
    actorId: "actor.fixture", perspective: "player",
    fetchImpl: async (input) => {
      const path = new URL(input).pathname;
      calls.push(path);
      assert.ok(!path.endsWith("/media-batch"), "player preview must not request ambient media");
      throw new Error("sheet transport failure");
    },
  });
  assert.equal(result.status, "error");
  assert.equal(result.media, null, "failed player preview cannot preserve a prior GM portrait");
  assert.equal(calls.length, 1);
});

test("sheet and dossier portrait outcomes remain independent across failed reads", async () => {
  const sheet = {
    version: 2, subject: { id: "actor.fixture", label: "Fixture Hero" },
    origin: { species: { id: "species.fixture", label: "Fixture Species" },
      background: { id: "background.fixture", label: "Fixture Background" } },
    classes: [{ id: "membership.fixture", name: "Fixture membership",
      class: { id: "class.fixture", label: "Fixture Class" }, level: 1, subclass: null }],
    inventory: { items: [], contentsDepth: 4, mayOmitDeeperContents: true },
    wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
  };
  const reads = [
    [readCanonicalCharacterSheet, characterSheetContract, sheet],
    [readCanonicalCharacter, characterDossierContract, characterDossier(sheet)],
  ];
  const cases = [
    ["http-failure-with-portrait", "http", 500, "success", true],
    ["thrown-failure-with-portrait", "throw", 0, "success", true],
    ["thrown-failure-media-unauthorized", "throw", 0, "unauthorized", true],
    ["http-failure-confirmed-empty", "http", 500, "empty", true],
    ["thrown-failure-transport-unknown", "throw", 0, "transport", false],
    ["http-failure-media-unauthorized", "http", 500, "unauthorized", true],
  ];
  for (const [read, contract, data] of reads) {
    for (const [name, sheetFailure, sheetStatus, mediaMode, clearable] of cases) {
      await test(`${contract.id}: ${name}`, async () => {
        const result = await read({
          origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "fixture",
          actorId: "actor.fixture", perspective: "dm",
          fetchImpl: async (input) => {
            const path = new URL(input).pathname;
            if (path.endsWith("/media-batch")) {
              if (mediaMode === "transport") return response(503, { code: "media-unavailable" });
              if (mediaMode === "unauthorized") return response(403, { code: "media-forbidden" });
              return response(200, { applicationId: "dnd2024", stateSpaceId: "fixture", items:
                mediaMode === "empty" ? [] : [{ entityId: "actor.fixture", attachments: [mediaAttachment()] }] });
            }
            if (sheetFailure === "throw") throw new Error("sheet transport failure");
            return response(sheetStatus, sheetFailure === "http" ? { code: "sheet-failed" } : {
              applicationId: "dnd2024", stateSpaceId: "fixture", qualifiedQueryId: contract.id,
              outputSchemaHash: contract.outputSchemaHash, stateSpaceFingerprint: "A".repeat(64),
              resolutionFingerprint: "B".repeat(64), resultFingerprint: "C".repeat(64),
              sourceRevisionFingerprint: "D".repeat(64), data,
            });
          },
        });
        assert.equal(result.status, "error");
        if (mediaMode === "success") assert.equal(result.media?.portrait.alt, "A reviewed portrait");
        else if (clearable) assert.equal(result.media, null);
        else assert.equal(result.media, undefined);
      });
    }
  }
});

test("sheet and dossier aborts observe concurrent media aborts without an unhandled portrait rejection", async () => {
  const sheet = {
    version: 2, subject: { id: "actor.fixture", label: "Fixture Hero" },
    origin: { species: { id: "species.fixture", label: "Fixture Species" },
      background: { id: "background.fixture", label: "Fixture Background" } },
    classes: [{ id: "membership.fixture", name: "Fixture membership",
      class: { id: "class.fixture", label: "Fixture Class" }, level: 1, subclass: null }],
    inventory: { items: [], contentsDepth: 4, mayOmitDeeperContents: true },
    wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
  };
  for (const [read, contract, data] of [
    [readCanonicalCharacterSheet, characterSheetContract, sheet],
    [readCanonicalCharacter, characterDossierContract, characterDossier(sheet)],
  ]) {
    const calls = [];
    await assert.rejects(read({
      origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "fixture",
      actorId: "actor.fixture", perspective: "dm",
      fetchImpl: async (input) => {
        calls.push(new URL(input).pathname);
        throw new DOMException("aborted", "AbortError");
      },
    }), { name: "AbortError" });
    assert.equal(calls.length, 2, `${contract.id} observes both sheet/dossier and media starts`);
    assert.ok(calls.some((path) => path.endsWith("/media-batch")));
    assert.ok(calls.some((path) => path.includes(`/read-models/${contract.id}`)));
  }
});

test("oversized portrait discovery is locally unavailable without hiding a valid sheet", async () => {
  const sheet = {
    version: 2, subject: { id: "actor.fixture", label: "Fixture Hero" },
    inventory: { items: [], contentsDepth: 4, mayOmitDeeperContents: true },
    wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
  };
  const result = await readCanonicalCharacterSheet({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "fixture",
    actorId: "actor.fixture", perspective: "dm",
    fetchImpl: async (input) => {
      if (new URL(input).pathname.endsWith("/media-batch")) return response(200, {
        applicationId: "dnd2024", stateSpaceId: "fixture", items: [],
        oversizedUnknownMediaMetadata: "x".repeat(4 * 1024 * 1024 + 1),
      });
      return response(200, { applicationId: "dnd2024", stateSpaceId: "fixture",
        qualifiedQueryId: characterSheetContract.id, outputSchemaHash: characterSheetContract.outputSchemaHash,
        stateSpaceFingerprint: "A".repeat(64), resolutionFingerprint: "B".repeat(64),
        resultFingerprint: "C".repeat(64), sourceRevisionFingerprint: "D".repeat(64), data: sheet });
    },
  });
  assert.equal(result.status, "ready");
  assert.equal(result.data.subject.id, "actor.fixture");
  assert.equal(result.media, undefined, "oversized media is unavailable, not a confirmed empty portrait");
});

test("canonical senses use named references and omit malformed measurements without rejecting the sheet", async () => {
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
    assert.equal(result.status, "ready", JSON.stringify(item.sense));
    assert.equal(result.data.subject.id, "actor.fixture", "unrelated identity remains usable");
    assert.deepEqual(result.data.origin.species, { id: "species.fixture", label: "Fixture Species" });
    if (item.ready) assert.deepEqual(result.data.senses, [item.sense]);
    else assert.equal(result.data.senses, undefined, "only the malformed sense section is unavailable");
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
  for (const contentUrl of [
    "https://elsewhere.test/content", "//elsewhere.test/content",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/media/../content",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/media/%2e%2e/content",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/media/a/content?query=/content",
  ]) assert.equal(projectMediaVisual(mediaRecord({ ...mediaAttachment(), contentUrl })), null);
});

test("media consumes its own fields without exact attachment shape or optional caption admission", () => {
  const attachment = { ...mediaAttachment(), extra: "ignored", caption: { changed: true }, alt: null, order: null };
  const projected = projectMediaVisual({ version: 88, attachments: [attachment] });
  assert.ok(projected.portrait);
  assert.equal(projected.portrait.alt, "Image");
  assert.equal(projected.portrait.imageUrl, attachment.contentUrl);
  assert.equal(JSON.stringify(projected).includes("ignored"), false);
});

test("location media isolates malformed owners and never treats an omitted owner as absent", async () => {
  for (const attachments of [undefined, [], [{ ...mediaAttachment("map"), width: -1 }]]) {
    const data = worldLocationScopeData();
    const result = await readRegisteredWorldLocationScope({
      origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
      scopeId: "realm-root-7", perspective: "player",
      fetchImpl: async (input) => new URL(input).pathname.endsWith("/media-batch")
        ? response(200, { applicationId: "dnd2024", stateSpaceId: "dnd2024-main", items: [
          { entityId: "realm-root-7", attachments: [{ ...mediaAttachment("map"), extra: "ignored" }] },
          ...(attachments === undefined ? [] : [{ entityId: "place-azure", attachments }]),
        ] }) : response(200, worldScopeEnvelope(data)),
    });
    assert.equal(result.items[0].mapVisualState, "ready");
    assert.equal(result.items[1].mapVisualState, attachments?.length === 0 ? "absent" : "unavailable");
  }
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

test("recorded play keeps valid continuity when individual participants or messages are malformed", () => {
  const projected = resolveRecordedPlaySituation({
    recentMessages: [
      { id: "play-message.good", ordinal: 1, role: "assistant", text: "The gate remains closed." },
      { id: "play-message.bad", ordinal: 2, role: "assistant", text: "" },
    ],
    currentSituation: {
      id: "play-situation.partial", status: "active", kind: "conversation",
      summary: "The gatekeeper is still deciding.",
      participants: [
        { name: "Orban", entityId: "actor.thalorien.brackenford.orban" },
        { name: "Unreadable", entityId: "bad id with spaces" },
      ],
      location: { name: "Brackenford", entityId: "location.thalorien.brackenford" },
    },
  }, ["location.thalorien.brackenford"]);
  assert.equal(projected.status, "ready");
  assert.equal(projected.coverage, "partial");
  assert.equal(projected.recorded.summary, "The gatekeeper is still deciding.");
  assert.deepEqual(projected.recorded.participants.map((participant) => participant.name), ["Orban"]);
  assert.deepEqual(projected.recorded.interactions.map((message) => message.text), ["The gate remains closed."]);
  assert.ok(projected.unavailableFields.includes("participants"));
  assert.ok(projected.unavailableFields.includes("interactions"));
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
          valueJson: JSON.stringify({ status: "open", observedAt: "new-additive-field" }),
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
        if (requested.searchParams.get("toEntityId") === originId) {
          return response(200, {
            items: [{ fromEntityId: routeId, toEntityId: originId, qualifiedKind: kind }],
          });
        }
        const targets = {
          "game.core.world.route.in-world": "world.thalorien",
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
        if (requested.searchParams.get("toEntityId") === originId) {
          return response(200, {
            items: [{ fromEntityId: routeId, toEntityId: originId, qualifiedKind: kind }],
          });
        }
        const targets = {
          "game.core.world.route.in-world": "world.thalorien",
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

test("known ways onward discover route candidates without probing knowledge subjects", async () => {
  let requests = 0;
  const originId = "location.thalorien.brackenford";
  const destinationId = "location.thalorien.crownmere";
  const routes = await readKnownOpenRoutes({
    fetchImpl: async (input) => {
      requests += 1;
      const requested = new URL(input);
      assert.equal(requested.pathname.endsWith("/relationships"), true);
      assert.equal(requested.searchParams.get("toEntityId"), originId);
      assert.equal(requested.searchParams.get("qualifiedKind"), "game.core.world.route.from");
      return response(200, { items: [] });
    },
    origin: "http://localhost:6217",
    entityRoot: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities",
    worldId: "world.thalorien",
    currentLocationId: originId,
    perspective: "dm",
    projectedKnowledge: {
      status: "ready",
      entries: [
        { text: "The World is known.", stance: "known", presentationKind: "statement",
          subject: { id: "world.thalorien", name: "Thalorien" } },
        { text: "Brackenford is known.", stance: "known", presentationKind: "statement",
          subject: { id: originId, name: "Brackenford" } },
        { text: "Crownmere is known.", stance: "known", presentationKind: "statement",
          subject: { id: destinationId, name: "Crownmere" } },
      ],
      locations: [],
    },
    locationDirectory: [
      { id: originId, name: "Brackenford" },
      { id: destinationId, name: "Crownmere" },
    ],
  });

  assert.deepEqual(routes, []);
  assert.equal(requests, 1);
});

test("Current route reads expose failed incoming relationships instead of claiming confirmed empty", async () => {
  const options = {
    fetchImpl: async () => response(503, { error: "temporarily unavailable" }),
    origin: "http://localhost:6217",
    entityRoot: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities",
    worldId: "world.thalorien",
    currentLocationId: "location.thalorien.brackenford",
    perspective: "dm",
    projectedKnowledge: { status: "ready", entries: [], locations: [] },
    locationDirectory: [],
  };
  assert.deepEqual(await readKnownOpenRoutes(options), []);
  await assert.rejects(
    readKnownOpenRoutes({ ...options, failOnUnavailable: true }),
    /route relationships are unavailable/u,
  );
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

test("conversation current scene retains identity and valid participants when summary or one participant is unavailable", async () => {
  const entityRoot = "/api/applications/dnd2024/state-spaces/dnd2024-main/entities";
  const value = await readConversationCurrentScene({
    fetchImpl: async (input) => {
      const requested = new URL(input);
      if (requested.pathname.endsWith("/media-batch")) return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main", items: [],
      });
      if (requested.pathname.endsWith("/interaction.partial"))
        return response(200, { entityId: "interaction.partial", name: "Partial parley" });
      if (requested.pathname.endsWith("/components/game.core.world.interaction")) return response(200, {
        entityId: "interaction.partial", qualifiedTypeId: "game.core.world.interaction",
        valueJson: JSON.stringify({ kind: "conversation", status: "accepted", producerNote: "inert" }),
      });
      if (requested.pathname.endsWith("/relationships")) return response(200, { items: [
        { fromEntityId: "interaction.partial", toEntityId: "actor.good", qualifiedKind: "game.core.world.interaction.participant" },
        { fromEntityId: "interaction.partial", toEntityId: "actor.bad", qualifiedKind: "game.core.world.interaction.participant" },
      ] });
      if (requested.pathname.endsWith("/actor.good")) return response(200, { entityId: "actor.good", name: "Good actor" });
      return response(404, {});
    },
    origin: "http://localhost:6217", entityRoot, conversationId: "interaction.partial", perspective: "dm",
    authorizedActorIds: new Set(["actor.good"]),
  });
  assert.equal(value.status, "ready");
  assert.equal(value.coverage, "partial");
  assert.equal(value.conversation.id, "interaction.partial");
  assert.deepEqual(value.conversation.participants, [{ id: "actor.good", name: "Good actor" }]);
  assert.equal(value.conversation.summary, undefined);
  assert.ok(value.unavailableFields.includes("summary"));
  assert.ok(value.unavailableFields.includes("participants"));
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
    assert.ok(calls.every((call) => call.searchParams.get("campaignId") === null));
    assert.ok(calls.every((call) => call.searchParams.get("perspective") === "player"));
  }
});

test("registered current play retains bound scene fields when resume and descriptive peers are partial", async () => {
  const scene = currentSceneData("exploration", [
    { key: "continue", label: "Continue", summary: "Continue from authoritative state." },
    { key: "bad", label: "", summary: "not admitted" },
    { key: "inspect", label: "Inspect", summary: 42 },
  ]);
  delete scene.location.summary;
  scene.location.kind = "astral-waypoint";
  const resume = campaignResumeData({
    locationId: scene.location.id, conversationId: null, encounterId: null,
  }, scene.affordances);
  for (const field of ["title", "premise", "partyGoals", "toneAndBoundaries"]) delete resume.campaign[field];
  resume.version = 99;
  const result = await readRegisteredCurrentPlay({
    origin: "http://localhost:6217", applicationId: "dnd2024",
    stateSpaceId: "dnd2024-main", campaignId: "campaign.thalorien", perspective: "player",
    fetchImpl: async (input) => {
      const requested = new URL(input);
      const contract = requested.pathname.endsWith(campaignResumeContract.id)
        ? campaignResumeContract : currentSceneContract;
      return response(200, currentPlayEnvelope(contract, contract === campaignResumeContract ? resume : scene));
    },
  });
  assert.equal(result.status, "ready");
  assert.equal(result.scene.location.id, scene.location.id);
  assert.equal(result.scene.location.kind, "astral-waypoint");
  assert.deepEqual(result.scene.affordances, [scene.affordances[0], { key: "inspect", label: "Inspect" }]);
  assert.equal(result.scene.coverage, "partial");
  assert.ok(result.scene.unavailableFields.includes("location.summary"));
  assert.ok(result.scene.unavailableFields.includes("affordances"));
});

test("T12 Current read evidence rejects a mixed binding but admits changed display-shape metadata", async () => {
  const scene = currentSceneData("exploration", []);
  const resume = campaignResumeData({ locationId: scene.location.id, conversationId: null, encounterId: null }, []);
  for (const mismatch of [null, "stateSpaceFingerprint", "resolutionFingerprint"]) {
    const result = await readRegisteredCurrentPlay({
      origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
      campaignId: "campaign.thalorien", perspective: "player",
      fetchImpl: async (input) => {
        const isResume = new URL(input).pathname.endsWith(campaignResumeContract.id);
        const record = currentPlayEnvelope(isResume ? campaignResumeContract : currentSceneContract,
          isResume ? resume : scene);
        if (!isResume) {
          record.outputSchemaHash = "e".repeat(64);
          if (mismatch) record[mismatch] = "f".repeat(64);
        }
        return response(200, record);
      },
    });
    assert.equal(result.status, mismatch ? "stale" : "ready");
    if (!mismatch) {
      assert.equal(result.projection.resume.qualifiedQueryId, campaignResumeContract.id);
      assert.equal(result.projection.scene.qualifiedQueryId, currentSceneContract.id);
      assert.equal(result.projection.scene.outputSchemaHash, "e".repeat(64), "actual observed metadata is retained, not pinned");
    }
  }
});

test("recorded continuity keeps identity and each independently readable history field", () => {
  const base = {
    currentSituation: {
      id: "play-situation.fields", status: "active", kind: "conversation",
      summary: "A valid recorded summary.",
      participants: [{ name: "Orban", entityId: "actor.orban" }],
    },
    recentMessages: [{ id: "play-message.one", ordinal: 1, role: "assistant", text: "The gate opens." }],
  };
  const missingParticipants = resolveRecordedPlaySituation({
    ...base, currentSituation: { ...base.currentSituation, participants: "not-a-list" },
  }, []);
  assert.equal(missingParticipants.status, "ready");
  assert.deepEqual(missingParticipants.recorded.participants, []);
  assert.deepEqual(missingParticipants.recorded.interactions.map((message) => message.id), ["play-message.one"]);
  assert.ok(missingParticipants.unavailableFields.includes("participants"));

  const missingInteractions = resolveRecordedPlaySituation({
    currentSituation: { ...base.currentSituation },
  }, []);
  assert.equal(missingInteractions.status, "ready");
  assert.deepEqual(missingInteractions.recorded.participants.map((participant) => participant.name), ["Orban"]);
  assert.deepEqual(missingInteractions.recorded.interactions, []);
  assert.ok(missingInteractions.unavailableFields.includes("interactions"));

  const missingSummary = resolveRecordedPlaySituation({
    ...base, currentSituation: { ...base.currentSituation, summary: undefined },
  }, []);
  assert.equal(missingSummary.status, "ready");
  assert.equal(missingSummary.recorded.summary, undefined);
  assert.deepEqual(missingSummary.recorded.participants.map((participant) => participant.name), ["Orban"]);
  assert.ok(missingSummary.unavailableFields.includes("summary"));

  const duplicateMessage = resolveRecordedPlaySituation({
    ...base,
    recentMessages: [
      ...base.recentMessages,
      { id: "play-message.one", ordinal: 2, role: "assistant", text: "A conflicting duplicate." },
    ],
  }, []);
  assert.equal(duplicateMessage.status, "ready");
  assert.deepEqual(duplicateMessage.recorded.interactions, []);
  assert.ok(duplicateMessage.unavailableFields.includes("interactions"));
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
