import assert from "node:assert/strict";
import test from "node:test";

import {
  CharacterResourceOwner,
  CHARACTER_DOSSIER_OBJECT_ID,
  TableResourceOwner,
  WorldResourceOwner,
  CAMPAIGN_SUMMARY_OBJECT_ID,
  CAMPAIGN_LOCATION_VISITS_OBJECT_ID,
  FACTION_DIRECTORY_OBJECT_ID,
  WORLD_CAMPAIGN_DIRECTORY_OBJECT_ID,
  isFactionDirectoryPage,
  type FactionDirectoryPage,
} from "../../src/data/object-resources";
import { currentDisplayFromUpdate, CurrentViewResourceOwner } from "../../src/data/current-resource-owner";
import { createHubObjectUiState, hubObjectUiReducer } from "../../src/data/hub-object-ui";
import { allocateCurrentRequestToken, createHubStore, currentActions, currentScope, selectCurrentDisplay } from "../../src/data/hub-store";
import type {
  CampaignReadModel,
  ConnectedCampaignEnvelope,
  HubEnvelope,
  InventoryContainerPageResult,
  InventoryContainerResult,
  PartyMemberReadModel,
  Perspective,
  ReadyHubEnvelope,
} from "../../src/data/hub-types";
import { ViewReadError } from "../../src/data/view-read-client";
import { connectedCampaignToHubEnvelope, mergeConnectedCampaignDetails } from "../../src/server/connected-hub-envelope";
import { readCurrentViewPatch, readDeferredCampaignDetails } from "../../src/server/game-server-context.js";
import { contract as campaignDetailsContract } from "../../src/server/campaign-details-contract.js";
import { contract as campaignLocationVisitsContract } from "../../src/server/campaign-location-visits-contract.js";

const evidence = (sourceRevisionFingerprint = "A".repeat(64)) => ({
  qualifiedQueryId: "dnd2024.query.faction-directory-page",
  stateSpaceFingerprint: "1".repeat(64),
  resolutionFingerprint: "2".repeat(64),
  outputSchemaHash: "3".repeat(64),
  resultFingerprint: "4".repeat(64),
  sourceRevisionFingerprint,
});

function campaign(perspective: Perspective, campaignId: string, status: "ready" | "denied" = "ready") {
  return { version: 1, status, perspective, campaignId } as unknown as HubEnvelope;
}

function isCampaign(value: unknown): value is HubEnvelope {
  return Boolean(value && typeof value === "object" &&
    (value as { version?: unknown }).version === 1 &&
    new Set(["ready", "denied"]).has(String((value as { status?: unknown }).status)));
}

function scope(
  perspective: Perspective = "dm",
  sourceRevisionFingerprint: string | null = null,
): ReadyHubEnvelope {
  return {
    applicationId: "dnd2024",
    stateSpaceId: "state.fixture",
    revision: "campaign.fixture",
    audience: { seat: "dm", perspective },
    contextSelection: { selectedCampaignId: "campaign.fixture", selectedWorldId: "world.fixture" },
    world: {
      id: "world.fixture",
      locationScopes: [],
      factionDirectory: sourceRevisionFingerprint === null ? undefined : {
        totalCount: 2, complete: false, nextCursor: "next", sourceRevisionFingerprint,
      },
    },
  } as unknown as ReadyHubEnvelope;
}

function scopeWithCampaignEvidence(sourceRevisionFingerprint: string, resultFingerprint = "4".repeat(64),
  resolutionFingerprint = "2".repeat(64)) {
  const envelope = scope();
  envelope.objectQueries = { campaignSummary: {
    qualifiedQueryId: "dnd2024.query.campaign-summary",
    stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint,
    outputSchemaHash: "3".repeat(64), resultFingerprint, sourceRevisionFingerprint,
  } };
  return envelope;
}

function currentReadEvidence(qualifiedQueryId: "dnd2024.query.campaign-resume" | "dnd2024.query.current-scene") {
  return {
    qualifiedQueryId,
    stateSpaceFingerprint: "1".repeat(64),
    resolutionFingerprint: "2".repeat(64),
    outputSchemaHash: "3".repeat(64),
    resultFingerprint: "4".repeat(64),
    sourceRevisionFingerprint: "A".repeat(64),
  };
}

function seededCurrentScope(perspective: Perspective = "dm", status: "ready" | "unavailable" = "ready") {
  const envelope = scope(perspective);
  const currentSituation = status === "ready"
    ? { status, kind: "exploration" as const, locationId: "location.old", affordances: [] }
    : { status, message: "No current scene." };
  Object.assign(envelope, {
    currentSituation,
    objectQueries: {
      currentPlay: {
        resume: currentReadEvidence("dnd2024.query.campaign-resume"),
        ...(status === "ready" ? { scene: currentReadEvidence("dnd2024.query.current-scene") } : {}),
      },
    },
  });
  Object.assign(envelope.world, {
    currentLocationId: "location.old",
    locations: [{ id: "location.old", name: "Old location" }],
  });
  return envelope;
}

function page(
  id = "faction.fixture",
  sourceRevisionFingerprint = "A".repeat(64),
): FactionDirectoryPage {
  return {
    coverage: "complete",
    factions: [{ id, name: id } as never],
    totalCount: 1,
    complete: true,
    nextCursor: null,
    sourceRevisionFingerprint,
    projection: evidence(sourceRevisionFingerprint),
  };
}

function character(id: string): PartyMemberReadModel {
  return {
    id, initials: "CF", name: `Character ${id}`, detail: "Summary", status: "Active participant",
    isCurrent: false, recordStatus: "Character summary", sheetStatus: "empty", inventoryStatus: "empty",
    sheetState: { status: "idle", data: null }, inventoryState: { status: "idle", data: null },
    sheet: [], knowledge: [], backstory: [], origin: [], inventory: [],
  };
}

function inventory(id: string): InventoryContainerResult {
  return {
    status: "ready", failureCategory: null, diagnosticId: `inventory-${id}`,
    data: {
      version: 2, container: { id, label: `Character ${id}` }, state: "ready", reasons: [], items: [],
      wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
      walletState: { status: "complete", reason: null },
      limits: { contentsDepth: 1, itemCount: 200, directComplete: true, recursiveComplete: false },
      projection: {
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        resultFingerprint: "4".repeat(64), sourceRevisionFingerprint: "A".repeat(64),
      },
    },
  };
}

function inventoryPage(containerId: string): InventoryContainerPageResult {
  return {
    status: "ready", failureCategory: null, diagnosticId: `inventory-page-${containerId}`,
    data: {
      version: 2, container: { id: containerId, label: containerId }, state: "ready", reasons: [], items: [],
      limits: { contentsDepth: 1, itemCount: 200, directComplete: true, recursiveComplete: false },
      projection: {
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        resultFingerprint: "4".repeat(64), sourceRevisionFingerprint: "A".repeat(64),
      },
    },
  };
}

function connectedCampaignSource(campaignId = "campaign.fixture"): ConnectedCampaignEnvelope {
  return {
    version: 1,
    status: "connected",
    applicationId: "dnd2024",
    stateSpaceId: "state.fixture",
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm"] },
    contextSelection: {
      selectedCampaignId: campaignId,
      selectedWorldId: "world.fixture",
      worlds: [{ id: "world.fixture", name: "Fixture World", campaigns: [{ id: campaignId, name: "Fixture Campaign" }] }],
    },
    campaign: {
      id: campaignId,
      name: "Fixture Campaign",
      status: "active",
      premise: "A production-shaped campaign fixture.",
      partyGoals: ["Reach the next chapter."],
      toneAndBoundaries: [],
      projection: {
        qualifiedQueryId: "dnd2024.query.campaign-summary",
        stateSpaceFingerprint: "1".repeat(64),
        resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: "3".repeat(64),
        resultFingerprint: "4".repeat(64),
        sourceRevisionFingerprint: "A".repeat(64),
      },
      chapters: [], arcs: [], sessions: [], visits: [],
    },
    actor: { id: "local-game-master", name: "Dungeon Master", state: null, entries: [] },
    party: [],
    knowledge: { status: "empty", entries: [], locations: [] },
    chronology: { status: "empty", perspective: "dm", entries: [] },
  };
}

function jsonResponse(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

async function productionCampaignDetails(
  source: ConnectedCampaignEnvelope,
  data: Record<string, unknown>,
): Promise<CampaignReadModel> {
  const details = await readDeferredCampaignDetails({
    origin: "http://localhost:6217",
    source,
    fetchImpl: async (input: RequestInfo | URL) => {
      const request = new URL(input);
      const contract = request.pathname.includes(campaignDetailsContract.id)
        ? campaignDetailsContract
        : campaignLocationVisitsContract;
      const responseData = contract === campaignDetailsContract
        ? data
        : { campaignTitle: source.campaign.name, visits: [], totalCount: 0, complete: true, nextCursor: null };
      return jsonResponse(200, {
        applicationId: source.applicationId,
        stateSpaceId: source.stateSpaceId,
        qualifiedQueryId: contract.id,
        stateSpaceFingerprint: "1".repeat(64),
        resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: contract.outputSchemaHash,
        resultFingerprint: "3".repeat(64),
        sourceRevisionFingerprint: "A".repeat(64),
        data: responseData,
      });
    },
  });
  return connectedCampaignToHubEnvelope(mergeConnectedCampaignDetails(source, details)).campaign;
}

test("Campaign transport isolates audiences and late perspective responses cannot refill confirmed state", async () => {
  const pending = new Map<string, (value: HubEnvelope) => void>();
  const state = new TableResourceOwner({
    readCampaign: ({ perspective, campaignId }) => new Promise((resolve) => {
      pending.set(`${perspective}:${campaignId}`, resolve);
    }),
    readFactionPage: async () => page(),
    readCampaignDetails: async () => { throw new Error("not used"); },
    readCampaignContext: async () => { throw new Error("not used"); },
    validateCampaign: isCampaign,
  });

  const obsolete = state.loadCampaign({ perspective: "dm", campaignId: "campaign.fixture" });
  const current = state.loadCampaign({ perspective: "player", campaignId: "campaign.fixture" });
  pending.get("player:campaign.fixture")?.(campaign("player", "campaign.fixture"));
  assert.equal((await current as unknown as { perspective: string }).perspective, "player");
  pending.get("dm:campaign.fixture")?.(campaign("dm", "campaign.fixture"));
  await assert.rejects(obsolete, (error) => error instanceof ViewReadError && error.category === "cancelled");
  assert.deepEqual(state.cacheMetrics(), { retainedEntries: 0, retainedBytes: 0 },
    "the transport coordinator owns no completed Campaign value");
});

test("object notices retire the relevant transport generation without retaining completed values", async () => {
  const envelope = scope();
  const state = new TableResourceOwner({
    readCampaign: async ({ perspective, campaignId }) => campaign(perspective, campaignId ?? "bound"),
    readFactionPage: async () => page(),
    readCampaignDetails: async () => { throw new Error("not used"); },
    readCampaignContext: async () => { throw new Error("not used"); },
    validateCampaign: isCampaign,
  });
  const campaignRequest = { perspective: "dm" as const, campaignId: "campaign.fixture" };
  const factionRequest = { envelope, cursor: null };
  await state.loadCampaign(campaignRequest);
  await state.loadFactionPage(factionRequest, new AbortController().signal);

  assert.equal(state.invalidateObject("dnd2024.object.unrelated"), false);
  assert.equal(state.invalidateObject(FACTION_DIRECTORY_OBJECT_ID), true);

  await state.loadFactionPage(factionRequest, new AbortController().signal);
  assert.equal(state.invalidateObject(CAMPAIGN_SUMMARY_OBJECT_ID), true);
  state.invalidateAll();
  assert.deepEqual(state.cacheMetrics(), { retainedEntries: 0, retainedBytes: 0 });
});

test("Faction pages reject incompatible and stale responses without retaining them", async () => {
  let result: unknown = page();
  const envelope = scope("dm", "A".repeat(64));
  const state = new TableResourceOwner({
    readCampaign: async ({ perspective, campaignId }) => campaign(perspective, campaignId ?? "bound"),
    readFactionPage: async () => result as FactionDirectoryPage,
    readCampaignDetails: async () => { throw new Error("not used"); },
    readCampaignContext: async () => { throw new Error("not used"); },
    validateCampaign: isCampaign,
  });
  const first = { envelope, cursor: null };
  await state.loadFactionPage(first, new AbortController().signal);
  assert.equal((await state.loadFactionPage(first, new AbortController().signal)).sourceRevisionFingerprint,
    "A".repeat(64));

  assert.equal(isFactionDirectoryPage({ ...page(), coverage: "partial" }), true,
    "field-local coverage is an allowed projection facet");
  assert.equal(isFactionDirectoryPage({ ...page(), coverage: "unknown" }), false,
    "unsupported coverage remains rejected");
  assert.equal(isFactionDirectoryPage({ ...page(), futureFacet: { state: "ready" } }), true,
    "additive display metadata does not invalidate the consumed faction fields");

  result = { ...page(), coverage: "unknown", futureFacet: { state: "ready" } } as unknown as FactionDirectoryPage;
  const normalized = await state.loadFactionPage(first, new AbortController().signal, false);
  assert.equal(normalized.coverage, "partial",
    "malformed optional coverage is retained as an honest partial facet");
  assert.equal(normalized.factions.length, 1);

  result = { ...page(), totalCount: 0, futureFacet: "ignored" };
  await assert.rejects(
    state.loadFactionPage({ envelope, cursor: "invalid-shape" }, new AbortController().signal, false),
    (error) => error instanceof ViewReadError && error.category === "incompatible-data",
  );

  result = page("faction.changed", "B".repeat(64));
  const next = { envelope, cursor: "next" };
  await assert.rejects(
    state.loadFactionPage(next, new AbortController().signal, false),
    (error) => error instanceof ViewReadError && error.category === "stale-data",
  );
  assert.equal(state.cacheMetrics().retainedBytes, 0,
    "incompatible and stale pages are never retained by the transport coordinator");
});

test("production Campaign details pass their presentation contract without transport retention", async () => {
  let detailReads = 0;
  let contextReads = 0;
  const source = connectedCampaignSource();
  const envelope = connectedCampaignToHubEnvelope(source);
  const transport = {
    version: 1,
    campaignId: source.campaign.id,
    chapters: [{
      id: "chapter.fixture.one", name: "Chapter One", status: "active",
      title: "The First Chapter", partyQuestion: "What will the party choose?",
      gmContext: "The decision changes the road ahead.",
    }],
    arcs: [{
      id: "arc.fixture.one", name: "Arc One", status: "active",
      title: "Mercy Has a Cost", partyStake: "The party's promise is at risk.",
      gmContext: "The rival is watching.",
    }],
    sessions: [],
  };
  const context = { section: "context" as const, contextSelection: {
    selectedCampaignId: "campaign.fixture", selectedWorldId: "world.fixture",
    worlds: [{ id: "world.fixture", name: "Fixture", campaigns: [{ id: "campaign.fixture", name: "Fixture" }] }],
  } };
  const state = new TableResourceOwner({
    readCampaign: async ({ perspective, campaignId }) => campaign(perspective, campaignId ?? "bound"),
    readFactionPage: async () => page(),
    readCampaignDetails: async () => {
      detailReads += 1;
      return productionCampaignDetails(source, transport);
    },
    readCampaignContext: async () => { contextReads += 1; return context; },
    validateCampaign: isCampaign,
  });
  const first = await state.loadCampaignDetails({ envelope });
  const second = await state.loadCampaignDetails({ envelope });
  assert.deepEqual(first, second);
  assert.equal(first.chapter, "The First Chapter");
  assert.equal(first.progress, "1 chapter · 1 arc");
  assert.deepEqual(first.threads.map((entry) => entry.title), ["The First Chapter", "Mercy Has a Cost"]);
  assert.equal(Object.hasOwn(first, "chapters"), false, "the cache owns the presentation model, not the transport DTO");
  await state.loadCampaignContext({ envelope });
  await state.loadCampaignContext({ envelope });
  assert.equal(detailReads, 2);
  assert.equal(contextReads, 2);
  assert.equal(state.invalidateObject(CAMPAIGN_LOCATION_VISITS_OBJECT_ID), true);
  await state.loadCampaignDetails({ envelope });
  await state.loadCampaignContext({ envelope });
  assert.equal(detailReads, 3);
  assert.equal(contextReads, 3);
  assert.equal(state.invalidateObject(WORLD_CAMPAIGN_DIRECTORY_OBJECT_ID), true);
  await state.loadCampaignContext({ envelope });
  assert.equal(contextReads, 4);
});

test("production Campaign conversion distinguishes a true empty campaign from multiple chapters and arcs", async () => {
  const source = connectedCampaignSource();
  const empty = await productionCampaignDetails(source, {
    version: 1, campaignId: source.campaign.id, chapters: [], arcs: [], sessions: [],
  });
  assert.equal(empty.progress, "Live campaign structure has not been recorded yet");
  assert.deepEqual(empty.adventureLog, []);
  assert.deepEqual(empty.threads, []);

  const populated = await productionCampaignDetails(source, {
    version: 1,
    campaignId: source.campaign.id,
    chapters: [
      { id: "chapter.one", name: "One", status: "active", title: "First road", partyQuestion: "Where next?" },
      { id: "chapter.two", name: "Two", status: "active", title: "Second road", partyQuestion: "Who follows?" },
    ],
    arcs: [
      { id: "arc.one", name: "One", status: "active", title: "First promise", partyStake: "A promise." },
      { id: "arc.two", name: "Two", status: "active", title: "Second promise", partyStake: "Another promise." },
    ],
    sessions: [],
  });
  assert.equal(populated.progress, "2 chapters · 2 arcs");
  assert.deepEqual(populated.threads.map((entry) => entry.title), [
    "First road", "Second road", "First promise", "Second promise",
  ]);
});

test("Campaign transport retains no completed response across scope replacement or age settings", async () => {
  const state = new TableResourceOwner({
    maximumEntries: 2,
    readCampaign: async ({ perspective, campaignId }) => campaign(perspective, campaignId ?? "bound"),
    readFactionPage: async () => page(),
    readCampaignDetails: async () => { throw new Error("not used"); },
    readCampaignContext: async () => { throw new Error("not used"); },
    validateCampaign: isCampaign,
  });
  for (const campaignId of ["one", "two", "three"]) {
    await state.loadCampaign({ perspective: "dm", campaignId });
  }
  assert.deepEqual(state.cacheMetrics(), { retainedEntries: 0, retainedBytes: 0 });

  const expiring = new TableResourceOwner({
    maximumAgeMs: 0,
    readCampaign: async ({ perspective, campaignId }) => campaign(perspective, campaignId ?? "bound"),
    readFactionPage: async () => page(),
    readCampaignDetails: async () => { throw new Error("not used"); },
    readCampaignContext: async () => { throw new Error("not used"); },
    validateCampaign: isCampaign,
  });
  await expiring.loadCampaign({ perspective: "player", campaignId: "one" });
  assert.deepEqual(expiring.cacheMetrics(), { retainedEntries: 0, retainedBytes: 0 });
});

test("a fast Campaign switch fences a late detail conversion from the replacement scope", async () => {
  const firstSource = connectedCampaignSource("campaign.one");
  const secondSource = connectedCampaignSource("campaign.two");
  const firstEnvelope = connectedCampaignToHubEnvelope(firstSource);
  const secondEnvelope = connectedCampaignToHubEnvelope(secondSource);
  const firstModel = await productionCampaignDetails(firstSource, {
    version: 1, campaignId: "campaign.one",
    chapters: [{ id: "chapter.one", name: "One", status: "active", title: "Old chapter", partyQuestion: "Old?" }],
    arcs: [], sessions: [],
  });
  const secondModel = await productionCampaignDetails(secondSource, {
    version: 1, campaignId: "campaign.two",
    chapters: [{ id: "chapter.two", name: "Two", status: "active", title: "New chapter", partyQuestion: "New?" }],
    arcs: [], sessions: [],
  });
  let finishFirst: (value: CampaignReadModel) => void = () => {};
  const state = new TableResourceOwner({
    readCampaign: async ({ campaignId }) => campaign("dm", campaignId ?? "bound"),
    readFactionPage: async () => page(),
    readCampaignDetails: ({ envelope }) => envelope.contextSelection.selectedCampaignId === "campaign.one"
      ? new Promise((resolve) => { finishFirst = resolve; })
      : Promise.resolve(secondModel),
    readCampaignContext: async () => { throw new Error("not used"); },
    validateCampaign: isCampaign,
  });

  await state.loadCampaign({ perspective: "dm", campaignId: "campaign.one" });
  const obsolete = state.loadCampaignDetails({ envelope: firstEnvelope });
  await state.loadCampaign({ perspective: "dm", campaignId: "campaign.two" });
  finishFirst(firstModel);
  await assert.rejects(obsolete, (error) =>
    error instanceof ViewReadError && error.category === "cancelled");
  const current = await state.loadCampaignDetails({ envelope: secondEnvelope });
  assert.equal(current.chapter, "New chapter");
});

test("Character requests deduplicate only while active; completed values belong to the confirmed store", async () => {
  let sheetReads = 0;
  let detailReads = 0;
  let inventoryReads = 0;
  let containerReads = 0;
  const owner = new CharacterResourceOwner({
    readSheet: async ({ actorId }) => { sheetReads += 1; return character(actorId); },
    readDetails: async ({ actorId }) => { detailReads += 1; return character(actorId); },
    readInventory: async ({ actorId }) => { inventoryReads += 1; return inventory(actorId); },
    readInventoryContainer: async ({ containerId }) => {
      containerReads += 1; return inventoryPage(containerId);
    },
  });
  const envelope = scope();
  const first = { envelope, actorId: "actor.first" };
  await Promise.all([owner.loadSheet(first), owner.loadSheet(first)]);
  await owner.loadDetails(first);
  await Promise.all([owner.loadInventory(first), owner.loadInventory(first)]);
  const bag = { ...first, containerId: "item.bag" };
  await Promise.all([owner.loadInventoryContainer(bag), owner.loadInventoryContainer(bag)]);
  await owner.loadSheet(first);
  assert.equal(sheetReads, 2);
  assert.equal(detailReads, 1);
  assert.equal(inventoryReads, 1);
  assert.equal(containerReads, 1);

  assert.equal(owner.invalidateObject("dnd2024.object.inventory-item-fixture"), true);
  await owner.loadSheet(first);
  await owner.loadDetails(first);
  await owner.loadInventory(first);
  await owner.loadInventoryContainer(bag);
  assert.equal(sheetReads, 3, "without a confirmed-store adapter, completed sheet values are not retained here");
  assert.equal(detailReads, 2, "an inventory transfer retires dossier-derived detail");
  assert.equal(inventoryReads, 2, "an inventory transfer retires the container projection");
  assert.equal(containerReads, 2, "an inventory transfer retires every scoped container page");

  await owner.loadSheet({ envelope, actorId: "actor.second" });
  await owner.loadSheet(first);
  assert.equal(sheetReads, 5, "request coordination never becomes a completed-response cache");

  assert.equal(owner.invalidateObject(CHARACTER_DOSSIER_OBJECT_ID), true);
  await owner.loadSheet(first);
  await owner.loadDetails(first);
  assert.equal(sheetReads, 6);
  assert.equal(detailReads, 3);
});

test("Character scope replacement fences cached actors across perspectives", async () => {
  let reads = 0;
  const owner = new CharacterResourceOwner({
    readSheet: async ({ actorId }) => { reads += 1; return character(actorId); },
    readDetails: async ({ actorId }) => character(actorId),
    readInventory: async ({ actorId }) => inventory(actorId),
  });
  await owner.loadSheet({ envelope: scope("dm"), actorId: "actor.first" });
  await owner.loadSheet({ envelope: scope("player"), actorId: "actor.first" });
  await owner.loadSheet({ envelope: scope("dm"), actorId: "actor.first" });
  assert.equal(reads, 3);
});

test("result revisions do not retain character values outside the confirmed store", async () => {
  let reads = 0;
  const owner = new CharacterResourceOwner({
    readSheet: async ({ actorId }) => { reads += 1; return character(actorId); },
    readDetails: async ({ actorId }) => character(actorId),
    readInventory: async ({ actorId }) => inventory(actorId),
  });
  const first = scopeWithCampaignEvidence("A".repeat(64), "4".repeat(64));
  const newerResult = scopeWithCampaignEvidence("B".repeat(64), "5".repeat(64));
  await owner.loadSheet({ envelope: first, actorId: "actor.first" });
  await owner.loadSheet({ envelope: newerResult, actorId: "actor.first" });
  assert.equal(reads, 2, "without a confirmed-store adapter, each completed request is read again");
  await owner.loadSheet({ envelope: scopeWithCampaignEvidence("C".repeat(64), "6".repeat(64), "7".repeat(64)),
    actorId: "actor.first" });
  assert.equal(reads, 3, "effective query resolution still fences active requests");
  assert.equal(owner.cacheMetrics().retainedBytes, 0);
});

test("World scope resources deduplicate exact locations and fence audience changes", async () => {
  let reads = 0;
  const owner = new WorldResourceOwner({
    readScope: async ({ scopeId }) => {
      reads += 1;
      return {
        section: "locations" as const,
        scopePage: { id: scopeId },
        world: {
          currentLocationId: "",
          map: { imageUrl: "", alt: "Map unavailable" },
          rootMapId: `map.live.${scopeId}`,
          maps: [{ id: `map.live.${scopeId}` }],
          regions: [], facts: [], locations: [],
          locationScopes: [{
            id: scopeId, name: scopeId, parentId: null, childIds: [], totalCount: 0,
            complete: true, nextCursor: null, sourceRevisionFingerprint: "A".repeat(64),
          }],
        },
        campaign: { mapOverlays: [] },
      } as unknown as import("../../src/data/object-resources").WorldScopeUpdate;
    },
    readInformation: async ({ section }) => ({
      section,
      ...(section === "people" ? { world: { locations: [], people: [] } } : {}),
      ...(section === "history" ? { world: { history: [] } } : {}),
      ...(section === "lore" ? {
        world: { lore: [] }, campaign: { quests: [], clues: [], mapOverlays: [] },
      } : {}),
    } as import("../../src/data/object-resources").WorldInformationUpdate),
  });
  const dm = scope("dm");
  await Promise.all([
    owner.loadScope({ envelope: dm, scopeId: "world.fixture" }),
    owner.loadScope({ envelope: dm, scopeId: "world.fixture" }),
  ]);
  await owner.loadScope({ envelope: dm, scopeId: "world.fixture" });
  assert.equal(reads, 2, "completed World pages are not retained by the transport coordinator");
  await owner.loadScope({ envelope: dm, scopeId: "location.region" });
  assert.equal(reads, 3, "a nested map scope is a separate bounded resource");
  await owner.loadScope({ envelope: scope("player"), scopeId: "world.fixture" });
  await owner.loadScope({ envelope: dm, scopeId: "world.fixture" });
  assert.equal(reads, 5, "audience replacement fences every prior-scope map resource");
});

test("World resources fence current-actor scope ABA and aborted cache consumers", async () => {
  let scopeReads = 0;
  let informationReads = 0;
  const owner = new WorldResourceOwner({
    readScope: async ({ scopeId }) => {
      scopeReads += 1;
      return {
        section: "locations", scopePage: { id: scopeId },
        world: {
          currentLocationId: "", map: { imageUrl: "", alt: "Map unavailable" },
          rootMapId: `map.live.${scopeId}`, maps: [{ id: `map.live.${scopeId}` }],
          regions: [], facts: [], locations: [],
          locationScopes: [{ id: scopeId, name: scopeId, parentId: null, childIds: [], totalCount: 0,
            complete: true, nextCursor: null, sourceRevisionFingerprint: "A".repeat(64) }],
        }, campaign: { mapOverlays: [] },
      } as unknown as import("../../src/data/object-resources").WorldScopeUpdate;
    },
    readInformation: async ({ section }) => {
      informationReads += 1;
      return { section, world: { locations: [], people: [] } } as
        import("../../src/data/object-resources").WorldInformationUpdate;
    },
  });
  const actorScope = (actorId: string) => {
    const envelope = scope("player");
    envelope.party = [{ id: actorId, isCurrent: true }] as ReadyHubEnvelope["party"];
    return envelope;
  };
  const first = actorScope("actor.first");
  const second = actorScope("actor.second");
  const scopeRequest = (envelope: ReadyHubEnvelope) => ({ envelope, scopeId: "world.fixture", cursor: null });
  const informationRequest = (envelope: ReadyHubEnvelope) => ({ envelope, section: "people" as const });

  await owner.loadScope(scopeRequest(first));
  await owner.loadInformation(informationRequest(first));
  const preAborted = new AbortController();
  preAborted.abort();
  await assert.rejects(owner.loadScope(scopeRequest(second), preAborted.signal),
    (error) => error instanceof ViewReadError && error.category === "cancelled");
  await assert.rejects(owner.loadInformation(informationRequest(second), preAborted.signal),
    (error) => error instanceof ViewReadError && error.category === "cancelled");
  await owner.loadScope(scopeRequest(first));
  await owner.loadInformation(informationRequest(first));
  assert.deepEqual([scopeReads, informationReads], [2, 2],
    "pre-aborted consumers cannot replace the active private scope or start a read");

  await owner.loadInformation(informationRequest(second));
  await owner.loadInformation(informationRequest(first));
  assert.equal(informationReads, 4,
    "A→B→A current-actor changes fence World information even when all other binding fields match");

  await owner.loadScope(scopeRequest(first));
  await owner.loadInformation(informationRequest(first));
  const cachedScopeAbort = new AbortController();
  const cachedScope = owner.loadScope(scopeRequest(first), cachedScopeAbort.signal);
  cachedScopeAbort.abort();
  await assert.rejects(cachedScope,
    (error) => error instanceof ViewReadError && error.category === "cancelled");
  const cachedInformationAbort = new AbortController();
  const cachedInformation = owner.loadInformation(informationRequest(first), cachedInformationAbort.signal);
  cachedInformationAbort.abort();
  await assert.rejects(cachedInformation,
    (error) => error instanceof ViewReadError && error.category === "cancelled");
  assert.deepEqual([scopeReads, informationReads], [4, 6],
    "an abort after transport start still prevents that result from becoming current");
});

test("World first-page cache identity ignores a newly materialized result revision", async () => {
  let reads = 0;
  const owner = new WorldResourceOwner({
    readScope: async ({ scopeId }) => {
      reads += 1;
      return { section: "locations", scopePage: { id: scopeId }, world: { currentLocationId: "", map: { imageUrl: "", alt: "Map unavailable" },
        rootMapId: `map.live.${scopeId}`, maps: [{ id: `map.live.${scopeId}` }], regions: [], facts: [], locations: [],
        locationScopes: [{ id: scopeId, name: scopeId, parentId: null, childIds: [], totalCount: 0,
          complete: true, nextCursor: null, sourceRevisionFingerprint: "A".repeat(64) }] },
        campaign: { mapOverlays: [] } } as unknown as import("../../src/data/object-resources").WorldScopeUpdate;
    },
    readInformation: async () => ({}) as import("../../src/data/object-resources").WorldInformationUpdate,
  });
  const first = scopeWithCampaignEvidence("A".repeat(64));
  const revisited = structuredClone(first);
  revisited.world.locationScopes = [{ id: "world.fixture", sourceRevisionFingerprint: "A".repeat(64) }] as never;
  await owner.loadScope({ envelope: first, scopeId: "world.fixture", cursor: null });
  await owner.loadScope({ envelope: revisited, scopeId: "world.fixture", cursor: null });
  assert.equal(reads, 2);
  assert.equal(owner.cacheMetrics().retainedBytes, 0);
});

test("World scope resources reject records from a different cached page", async () => {
  const owner = new WorldResourceOwner({
    readScope: async ({ scopeId }) => ({
      section: "locations", scopePage: { id: scopeId },
      world: {
        currentLocationId: "", map: { imageUrl: "", alt: "Map unavailable" },
        rootMapId: `map.live.${scopeId}`, maps: [{ id: `map.live.${scopeId}` }], regions: [], facts: [],
        locations: [{ id: "location.from-another-page" }],
        locationScopes: [{ id: scopeId, name: scopeId, parentId: null, childIds: [], totalCount: 0,
          complete: true, nextCursor: null, sourceRevisionFingerprint: "A".repeat(64) }],
      },
      campaign: { mapOverlays: [] },
    }) as unknown as import("../../src/data/object-resources").WorldScopeUpdate,
    readInformation: async () => ({}) as import("../../src/data/object-resources").WorldInformationUpdate,
  });

  await assert.rejects(owner.loadScope({ envelope: scope("dm"), scopeId: "world.fixture" }),
    (error) => error instanceof ViewReadError && error.category === "incompatible-data");
  assert.equal(owner.cacheMetrics().retainedBytes, 0, "the foreign page body is never retained");
});

test("World information transport deduplicates active reads and fences observers", async () => {
  const reads = { people: 0, lore: 0, history: 0 };
  const owner = new WorldResourceOwner({
    readScope: async () => ({}) as import("../../src/data/object-resources").WorldScopeUpdate,
    readInformation: async ({ section }) => {
      reads[section] += 1;
      if (section === "people") return {
        section, world: { locations: [], people: [] },
      } as import("../../src/data/object-resources").WorldInformationUpdate;
      if (section === "history") return {
        section, world: { history: [] },
      } as import("../../src/data/object-resources").WorldInformationUpdate;
      return {
        section, world: { lore: [] }, campaign: { quests: [], clues: [], mapOverlays: [] },
      } as import("../../src/data/object-resources").WorldInformationUpdate;
    },
  });
  const dm = scope("dm");
  await Promise.all([
    owner.loadInformation({ envelope: dm, section: "people" }),
    owner.loadInformation({ envelope: dm, section: "people" }),
  ]);
  await owner.loadInformation({ envelope: dm, section: "lore" });
  await owner.loadInformation({ envelope: dm, section: "history" });
  await owner.loadInformation({ envelope: dm, section: "lore" });
  assert.deepEqual(reads, { people: 1, lore: 2, history: 1 });

  owner.invalidateAll("object-change");
  await owner.loadInformation({ envelope: dm, section: "people" });
  assert.equal(reads.people, 2, "a relevant object change starts a new directory request");

  const actor = scope("player");
  await owner.loadInformation({ envelope: actor, section: "people" });
  await owner.loadInformation({ envelope: dm, section: "people" });
  assert.equal(reads.people, 4, "observer replacement fences private information");
});

test("World People resources accept the declared 1,000 locations plus 2,000 people bound", async () => {
  const update = (locations: number, people: number) => ({
    section: "people" as const,
    world: {
      locations: Array.from({ length: locations }, (_, index) => ({ id: `location-${index}` })),
      people: Array.from({ length: people }, (_, index) => ({ id: `person-${index}` })),
      peopleDirectory: { totalCount: people, hierarchyComplete: true,
        sourceRevisionFingerprint: "A".repeat(64) },
    },
  }) as unknown as import("../../src/data/object-resources").WorldInformationUpdate;
  const owner = new WorldResourceOwner({
    readScope: async () => ({}) as import("../../src/data/object-resources").WorldScopeUpdate,
    readInformation: async () => update(1_000, 2_000),
  });

  const result = await owner.loadInformation({ envelope: scope("dm"), section: "people" });
  assert.equal(result.world.locations.length + result.world.people.length, 3_000);

  const overflow = new WorldResourceOwner({
    readScope: async () => ({}) as import("../../src/data/object-resources").WorldScopeUpdate,
    readInformation: async () => update(1_001, 2_000),
  });
  await assert.rejects(
    overflow.loadInformation({ envelope: scope("dm"), section: "people" }),
    (error) => error instanceof ViewReadError && error.category === "incompatible-data",
  );
});

test("Current View deduplicates one campaign resource and fences late observer responses", async () => {
  type CurrentUpdate = import("../../src/data/object-resources").CurrentViewUpdate;
  const pending = new Map<string, (value: CurrentUpdate) => void>();
  let reads = 0;
  const update = (locationId: string): CurrentUpdate => ({
    section: "current",
    currentSituation: locationId
      ? { status: "ready", kind: "exploration", locationId, affordances: [] }
      : { status: "unavailable", message: "No current scene." },
    world: { currentLocationId: locationId, locations: locationId ? [{ id: locationId, name: locationId }] : [] },
    campaign: { mapOverlays: [] },
  });
  const owner = new CurrentViewResourceOwner({
    store: createHubStore(),
    readCurrent: ({ envelope }) => {
      reads += 1;
      return new Promise((resolve) => pending.set(envelope.audience.perspective, resolve));
    },
  });
  const dmEnvelope = scope("dm");
  const dmFirst = owner.loadCurrent({ envelope: dmEnvelope });
  const dmDuplicate = owner.loadCurrent({ envelope: dmEnvelope });
  assert.equal(reads, 1);

  const playerEnvelope = scope("player");
  const obsolete = dmFirst;
  const player = owner.loadCurrent({ envelope: playerEnvelope });
  pending.get("player")?.(update("location.player"));
  assert.equal((await player).location?.id, "location.player");
  pending.get("dm")?.(update("location.dm"));
  await assert.rejects(obsolete, (error) =>
    error instanceof ViewReadError && error.category === "cancelled");
  await assert.rejects(dmDuplicate, (error) =>
    error instanceof ViewReadError && error.category === "cancelled");

  await owner.loadCurrent({ envelope: playerEnvelope });
  assert.equal(reads, 2, "the Player result remains cached after the late DM response");
  assert.equal(owner.invalidateObject(CAMPAIGN_SUMMARY_OBJECT_ID), true);
  const refreshed = owner.loadCurrent({ envelope: playerEnvelope });
  pending.get("player")?.(update("location.changed"));
  assert.equal((await refreshed).location?.id, "location.changed");
  assert.equal(reads, 3);
});

test("Current View projects malformed optional fields before retaining a deferred response", async () => {
  type CurrentUpdate = import("../../src/data/object-resources").CurrentViewUpdate;
  const owner = new CurrentViewResourceOwner({
    store: createHubStore(),
    readCurrent: async () => ({
      section: "current",
      currentSituation: {
        status: "ready",
        kind: "recorded",
        recorded: {
          id: "play-situation.sparse",
          kind: "conversation",
          participants: [{ id: "actor.keep", name: "Kept" }, { id: "actor.keep", name: "Duplicate" }, { id: "actor.no-name" }],
          interactions: [{ id: "message.keep", role: "player", text: "Useful", ordinal: 1 }, { id: "message.bad", role: "other", text: "Withheld" }],
        },
      },
      world: { currentLocationId: "location.current", locations: [] },
      campaign: { mapOverlays: [] },
    }) as unknown as CurrentUpdate,
  });
  const result = await owner.loadCurrent({ envelope: scope("dm") });
  assert.equal(result.situation.status, "ready");
  if (result.situation.status === "ready" && result.situation.kind === "recorded") {
    assert.equal(result.situation.recorded.id, "play-situation.sparse");
    assert.deepEqual(result.situation.recorded.participants?.map((entry) => entry.id), ["actor.no-name"]);
    assert.equal(result.situation.recorded.participants?.[0]?.name, "Unnamed participant");
    assert.deepEqual(result.situation.recorded.interactions?.map((entry) => entry.id), ["message.keep"]);
    assert.ok(result.situation.unavailableFields?.includes("participants"));
    assert.ok(result.situation.unavailableFields?.includes("interactions"));
  }

  const absentScene = new CurrentViewResourceOwner({
    store: createHubStore(),
    readCurrent: async () => ({
      section: "current", currentSituation: null,
      world: { currentLocationId: "location.current", locations: [] }, campaign: { mapOverlays: [] },
    }) as unknown as CurrentUpdate,
  });
  assert.equal((await absentScene.loadCurrent({ envelope: scope("dm") })).situation.status, "unavailable");
});

test("Current View safely withholds malformed deferred location rows and route coverage", async () => {
  type CurrentUpdate = import("../../src/data/object-resources").CurrentViewUpdate;
  const owner = new CurrentViewResourceOwner({
    store: createHubStore(),
    readCurrent: async () => ({
      section: "current",
      currentSituation: { status: "ready", kind: "exploration", locationId: "location.keep", affordances: [], routesCoverage: "complete" },
      world: {
        currentLocationId: "location.keep",
        locations: [null, "not a location", {
          id: "location.keep", name: "Kept location", mapAnchor: { x: 101, y: 10 }, routes: [{ destination: "Safe destination", detail: 42 }], people: [null], observations: ["Still useful", 42],
          dmSecret: "Still scope-authorized", media: { scene: { imageUrl: "javascript:alert(1)", alt: "Unsafe", width: 48, height: 48 } },
        }],
      },
      campaign: { mapOverlays: [null, "not an overlay", { id: "overlay.keep" }] },
    }) as unknown as CurrentUpdate,
  });
  const result = await owner.loadCurrent({ envelope: scope("dm") });
  assert.ok(result.location);
  const location = result.location as unknown as { routes?: unknown[]; observations?: unknown[]; media?: unknown; mapAnchor?: unknown };
  assert.deepEqual(location.routes, [{ destination: "Safe destination", detail: "Route details unavailable." }]);
  assert.deepEqual(location.observations, ["Still useful"]);
  assert.equal(location.media, undefined, "untrusted location media is never forwarded to the image component");
  assert.equal(location.mapAnchor, undefined, "out-of-range anchors are omitted instead of being clamped");
  assert.equal((location as { dmSecret?: unknown }).dmSecret, "Still scope-authorized");
  assert.equal(result.situation.status, "ready");
  if (result.situation.status === "ready" && result.situation.kind === "exploration") {
    assert.equal(result.situation.routesCoverage, "partial", "missing or invalid routes never imply known zero");
    assert.ok(result.situation.unavailableFields?.includes("routes"));
  }
});

test("Current Redux owns only the selected location, preserves cache age, and fences a cancelled joined read", async () => {
  type CurrentUpdate = import("../../src/data/object-resources").CurrentViewUpdate;
  const envelope = scope("dm");
  envelope.party = [{ id: "actor.current", isCurrent: true }] as ReadyHubEnvelope["party"];
  const store = createHubStore();
  let reads = 0;
  let resolve: ((value: CurrentUpdate) => void) | null = null;
  const response = (): CurrentUpdate => ({ section: "current",
    currentSituation: { status: "ready", kind: "exploration", locationId: "location.selected", affordances: [] },
    world: { currentLocationId: "location.selected", locations: [
      { id: "location.selected", name: "Selected" }, { id: "location.other", name: "Not a directory cache" },
    ] }, campaign: { mapOverlays: [{ id: "overlay.unrelated" }] } } as unknown as CurrentUpdate);
  const owner = new CurrentViewResourceOwner({ store, readCurrent: async () => {
    reads += 1;
    return new Promise<CurrentUpdate>((done) => { resolve = done; });
  } });
  const first = owner.loadCurrent({ envelope });
  const cancelledController = new AbortController();
  const joined = owner.loadCurrent({ envelope }, cancelledController.signal);
  cancelledController.abort();
  await assert.rejects(joined, (error) => error instanceof ViewReadError && error.category === "cancelled");
  resolve?.(response());
  const value = await first;
  assert.equal(value.location?.id, "location.selected");
  assert.equal(reads, 1);
  const scoped = currentScope(envelope);
  const before = store.getState().current.entry?.confirmedAt;
  assert.equal(selectCurrentDisplay(scoped)(store.getState())?.location?.id, "location.selected");
  await owner.loadCurrent({ envelope });
  assert.equal(reads, 1, "a Redux hit does not launch a replacement transport");
  assert.equal(store.getState().current.entry?.confirmedAt, before, "a hit never renews confirmation freshness");
  assert.equal("world" in (store.getState().current.entry?.value ?? {}), false,
    "Current retains no World directory fragment");
});

test("Current rejects pre-aborted loads without changing scope and fences delayed bootstrap staging", async () => {
  type CurrentUpdate = import("../../src/data/object-resources").CurrentViewUpdate;
  const store = createHubStore();
  const dm = seededCurrentScope("dm");
  const player = scope("player");
  for (const envelope of [dm, player]) {
    Object.assign(envelope.world, { currentLocationId: "location.old", locations: [] });
    Object.assign(envelope, { campaign: { mapOverlays: [] } });
  }
  const update = { section: "current", currentSituation: { status: "unavailable", message: "No scene." },
    world: { currentLocationId: "location.old", locations: [{ id: "location.old", name: "Old" }] },
    campaign: { mapOverlays: [] } } as unknown as CurrentUpdate;
  const owner = new CurrentViewResourceOwner({ store, readCurrent: async () => update });
  owner.replaceScope(dm);
  const controller = new AbortController();
  controller.abort();
  await assert.rejects(owner.loadCurrent({ envelope: player }, controller.signal),
    (error) => error instanceof ViewReadError && error.category === "cancelled");
  assert.equal(store.getState().current.scope, currentScope(dm), "a pre-aborted read cannot replace private scope");
  await owner.loadCurrent({ envelope: dm });
  await assert.rejects(owner.loadCurrent({ envelope: dm }, controller.signal),
    (error) => error instanceof ViewReadError && error.category === "cancelled");

  const eligibleSeed = await owner.seedBootstrap(dm);
  assert.equal(eligibleSeed?.situation.status, "ready",
    "the synthetic Current evidence passes the bootstrap gate before the ABA fence is exercised");
  owner.invalidateAll();
  const staging = owner.seedBootstrap(dm);
  owner.replaceScope(player, true);
  owner.replaceScope(dm, true);
  await staging;
  assert.equal(selectCurrentDisplay(currentScope(dm))(store.getState()), null,
    "a delayed A bootstrap cannot revive after A→B→A scope generations");
  const unplaced = currentDisplayFromUpdate({ ...update,
    currentSituation: { status: "unavailable", message: "No current scene." } } as CurrentUpdate);
  assert.equal(unplaced.location, null, "an unavailable scene never borrows World’s prior location");
});

test("a denied Current read fences an older owner bootstrap seed", async () => {
  type CurrentUpdate = import("../../src/data/object-resources").CurrentViewUpdate;
  const store = createHubStore();
  const source = seededCurrentScope("dm");
  const owner = new CurrentViewResourceOwner({ store, readCurrent: async () => ({
    section: "current", currentSituation: { status: "unavailable", message: "No scene." },
    world: { locations: [] },
  } as unknown as CurrentUpdate) });
  const scopeKey = owner.replaceScope(source);
  const eligibleSeed = await owner.seedBootstrap(source);
  assert.equal(eligibleSeed?.location?.id, "location.old",
    "the synthetic Current evidence passes the bootstrap gate before the denial fence is exercised");
  owner.invalidateAll();
  const staging = owner.seedBootstrap(source);
  const deniedToken = allocateCurrentRequestToken();
  store.dispatch(currentActions.currentRequestStarted({ scope: scopeKey, requestToken: deniedToken }));
  store.dispatch(currentActions.currentDenied({ scope: scopeKey, requestToken: deniedToken }));
  await staging;
  assert.equal(selectCurrentDisplay(scopeKey)(store.getState()), null,
    "an older async bootstrap cannot revive a Current projection after authorization denial");
});

test("unloaded Current bootstraps without query evidence are not cached and load the registered query", async () => {
  type CurrentUpdate = import("../../src/data/object-resources").CurrentViewUpdate;
  let reads = 0;
  const readyPlaceholder = scope("dm");
  Object.assign(readyPlaceholder, {
    currentSituation: { status: "ready", kind: "exploration", locationId: "location.unloaded", affordances: [] },
  });
  Object.assign(readyPlaceholder.world, {
    currentLocationId: "location.unloaded", locations: [{ id: "location.unloaded", name: "Unloaded location" }],
  });
  const unavailablePlaceholder = seededCurrentScope("dm", "unavailable");
  delete unavailablePlaceholder.objectQueries;

  for (const [label, envelope] of [["ready", readyPlaceholder], ["unavailable", unavailablePlaceholder]] as const) {
    const store = createHubStore();
    const owner = new CurrentViewResourceOwner({
      store,
      readCurrent: async () => {
        reads += 1;
        return {
          section: "current",
          currentSituation: { status: "ready", kind: "exploration", locationId: `location.registered.${label}`, affordances: [] },
          world: { locations: [{ id: `location.registered.${label}`, name: "Registered location" }] },
        } as CurrentUpdate;
      },
    });

    assert.equal(await owner.seedBootstrap(envelope), null);
    assert.equal(selectCurrentDisplay(currentScope(envelope))(store.getState()), null,
      `${label} staging without registered query evidence never becomes a fresh Current cache entry`);
    assert.equal((await owner.loadCurrent({ envelope })).location?.id, `location.registered.${label}`);
  }
  assert.equal(reads, 2, "Current performs its registered read after each evidence-free bootstrap");
});

test("a forced same-scope replacement cannot let an evidence-free Current bootstrap suppress its next query", async () => {
  type CurrentUpdate = import("../../src/data/object-resources").CurrentViewUpdate;
  const seeded = seededCurrentScope("dm");
  let reads = 0;
  const owner = new CurrentViewResourceOwner({
    store: createHubStore(),
    readCurrent: async () => {
      reads += 1;
      return {
        section: "current",
        currentSituation: { status: "ready", kind: "exploration", locationId: "location.refreshed", affordances: [] },
        world: { locations: [{ id: "location.refreshed", name: "Refreshed location" }] },
      } as CurrentUpdate;
    },
  });
  assert.equal((await owner.seedBootstrap(seeded))?.location?.id, "location.old");

  owner.replaceScope(seeded, true);
  const unloaded = scope("dm");
  Object.assign(unloaded, {
    currentSituation: { status: "ready", kind: "exploration", locationId: "location.unloaded", affordances: [] },
  });
  Object.assign(unloaded.world, { currentLocationId: "location.unloaded", locations: [] });
  assert.equal(currentScope(unloaded), currentScope(seeded), "the replacement is within the same private scope");
  assert.equal(await owner.seedBootstrap(unloaded), null);
  assert.equal((await owner.loadCurrent({ envelope: unloaded })).location?.id, "location.refreshed");
  assert.equal(reads, 1, "the replacement performs a real Current query rather than restoring placeholder staging");
});

test("an authoritative unavailable Current with resume evidence seeds an empty result without refetching", async () => {
  type CurrentUpdate = import("../../src/data/object-resources").CurrentViewUpdate;
  const envelope = seededCurrentScope("dm", "unavailable");
  let reads = 0;
  const owner = new CurrentViewResourceOwner({
    store: createHubStore(),
    readCurrent: async () => {
      reads += 1;
      return {
        section: "current",
        currentSituation: { status: "ready", kind: "exploration", locationId: "location.unexpected", affordances: [] },
        world: { locations: [] },
      } as CurrentUpdate;
    },
  });

  const seeded = await owner.seedBootstrap(envelope);
  assert.equal(seeded?.situation.status, "unavailable");
  assert.equal(seeded?.location, null, "an authoritative no-scene result is genuinely empty");
  assert.equal((await owner.loadCurrent({ envelope })).situation.status, "unavailable");
  assert.equal(reads, 0, "the confirmed unavailable result remains a valid Current cache hit");
});

test("the production Current adapter clears authorization loss but retains transient failure", async () => {
  type CurrentUpdate = import("../../src/data/object-resources").CurrentViewUpdate;
  const envelope = seededCurrentScope("dm");
  const deniedStore = createHubStore();
  const deniedOwner = new CurrentViewResourceOwner({
    store: deniedStore,
    readCurrent: async () => await readCurrentViewPatch({
      origin: "http://localhost:6217",
      source: connectedCampaignSource(),
      fetchImpl: async () => jsonResponse(403, {}),
    }) as CurrentUpdate,
  });
  await deniedOwner.seedBootstrap(envelope);
  const scopeKey = currentScope(envelope);
  assert.equal(selectCurrentDisplay(scopeKey)(deniedStore.getState())?.location?.id, "location.old");
  await assert.rejects(deniedOwner.loadCurrent({ envelope }, undefined, false),
    (error) => error instanceof ViewReadError && error.category === "authorization");
  assert.equal(selectCurrentDisplay(scopeKey)(deniedStore.getState()), null,
    "the real registered-query forbidden path revokes the complete prior Current projection");

  const transientStore = createHubStore();
  const transientOwner = new CurrentViewResourceOwner({
    store: transientStore,
    readCurrent: async () => { throw new ViewReadError("transport", "Current is temporarily unavailable."); },
  });
  await transientOwner.seedBootstrap(envelope);
  await assert.rejects(transientOwner.loadCurrent({ envelope }, undefined, false),
    (error) => error instanceof ViewReadError && error.category === "transport");
  assert.equal(selectCurrentDisplay(scopeKey)(transientStore.getState())?.location?.id, "location.old",
    "a transient Current failure keeps the last authorized scene visible without making it fresh");
  assert.equal(transientStore.getState().current.entry?.fresh, false);
});

test("local edit state remains pending through submit and retains failed drafts until server confirmation", () => {
  let state = createHubObjectUiState("faction.one");
  state = hubObjectUiReducer(state,
    { type: "edit-staged", objectId: CAMPAIGN_SUMMARY_OBJECT_ID, draft: { premise: "Mercy has a cost." } });
  state = hubObjectUiReducer(state, { type: "write-submitted", objectId: CAMPAIGN_SUMMARY_OBJECT_ID });
  assert.equal(state.edits[CAMPAIGN_SUMMARY_OBJECT_ID].status, "pending");
  assert.deepEqual(state.edits[CAMPAIGN_SUMMARY_OBJECT_ID].draft, { premise: "Mercy has a cost." });

  const prematureConfirmation = hubObjectUiReducer(
    hubObjectUiReducer(state, {
      type: "write-failed", objectId: CAMPAIGN_SUMMARY_OBJECT_ID, error: "The source revision changed.",
    }),
    { type: "write-confirmed", objectId: CAMPAIGN_SUMMARY_OBJECT_ID },
  );
  assert.equal(prematureConfirmation.edits[CAMPAIGN_SUMMARY_OBJECT_ID].status, "failed");
  assert.equal(prematureConfirmation.edits[CAMPAIGN_SUMMARY_OBJECT_ID].error, "The source revision changed.");

  state = hubObjectUiReducer(prematureConfirmation,
    { type: "edit-cancelled", objectId: CAMPAIGN_SUMMARY_OBJECT_ID });
  assert.equal(state.edits[CAMPAIGN_SUMMARY_OBJECT_ID], undefined,
    "a failed local draft can be rolled back without changing server data");
  state = hubObjectUiReducer(state,
    { type: "edit-staged", objectId: CAMPAIGN_SUMMARY_OBJECT_ID, draft: { premise: "Mercy has a cost." } });
  state = hubObjectUiReducer(state, { type: "write-submitted", objectId: CAMPAIGN_SUMMARY_OBJECT_ID });

  state = hubObjectUiReducer(state, { type: "write-confirmed", objectId: CAMPAIGN_SUMMARY_OBJECT_ID });
  assert.equal(state.edits[CAMPAIGN_SUMMARY_OBJECT_ID], undefined);
  state = hubObjectUiReducer(state, { type: "scope-replaced", factionId: "faction.two" });
  assert.deepEqual(state, createHubObjectUiState("faction.two"));
});
