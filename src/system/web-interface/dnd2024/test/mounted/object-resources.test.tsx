import assert from "node:assert/strict";
import test from "node:test";

import {
  CharacterResourceOwner,
  CHARACTER_DOSSIER_OBJECT_ID,
  CurrentViewResourceOwner,
  TableResourceOwner,
  WorldResourceOwner,
  CAMPAIGN_SUMMARY_OBJECT_ID,
  CAMPAIGN_LOCATION_VISITS_OBJECT_ID,
  FACTION_DIRECTORY_OBJECT_ID,
  WORLD_CAMPAIGN_DIRECTORY_OBJECT_ID,
  type FactionDirectoryPage,
} from "../../src/data/object-resources";
import { createHubObjectUiState, hubObjectUiReducer } from "../../src/data/hub-object-ui";
import type {
  CampaignReadModel,
  ConnectedCampaignEnvelope,
  HubEnvelope,
  InventoryContainerResult,
  PartyMemberReadModel,
  Perspective,
  ReadyHubEnvelope,
} from "../../src/data/hub-types";
import { ViewReadError } from "../../src/data/view-read-client";
import { connectedCampaignToHubEnvelope, mergeConnectedCampaignDetails } from "../../src/server/connected-hub-envelope";
import { readDeferredCampaignDetails } from "../../src/server/game-server-context.js";
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

function page(
  id = "faction.fixture",
  sourceRevisionFingerprint = "A".repeat(64),
): FactionDirectoryPage {
  return {
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
      version: 1, owner: { id, label: `Character ${id}` }, state: "ready", reasons: [], items: [],
      wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
      limits: { contentsDepth: 4, itemCount: 100, complete: true },
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

test("Campaign requests isolate audiences and late perspective responses cannot refill the cache", async () => {
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
  assert.equal(state.peekCampaign({ perspective: "dm", campaignId: "campaign.fixture" }), null);
  assert.notEqual(state.peekCampaign({ perspective: "player", campaignId: "campaign.fixture" }), null);
});

test("object notices invalidate only their migrated cache and reconnect invalidates both", async () => {
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
  assert.notEqual(state.peekCampaign(campaignRequest), null);
  assert.notEqual(state.peekFactionPage(factionRequest), null);
  assert.equal(state.invalidateObject(FACTION_DIRECTORY_OBJECT_ID), true);
  assert.notEqual(state.peekCampaign(campaignRequest), null);
  assert.equal(state.peekFactionPage(factionRequest), null);

  await state.loadFactionPage(factionRequest, new AbortController().signal);
  assert.equal(state.invalidateObject(CAMPAIGN_SUMMARY_OBJECT_ID), true);
  assert.equal(state.peekCampaign(campaignRequest), null);
  assert.notEqual(state.peekFactionPage(factionRequest), null);
  state.invalidateAll();
  assert.equal(state.peekFactionPage(factionRequest), null);
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
  assert.equal(state.peekFactionPage(first)?.value.sourceRevisionFingerprint, "A".repeat(64));

  result = { ...page(), privateField: "must not enter cache" };
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
  assert.equal(state.peekFactionPage(next), null);
  assert.equal(state.peekFactionPage(first), null, "a stale page retires the whole Factions query cache");
});

test("production Campaign details pass their presentation contract and use an independent bounded resource", async () => {
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
  assert.equal(first, second);
  assert.equal(first.chapter, "The First Chapter");
  assert.equal(first.progress, "1 chapter · 1 arc");
  assert.deepEqual(first.threads.map((entry) => entry.title), ["The First Chapter", "Mercy Has a Cost"]);
  assert.equal(Object.hasOwn(first, "chapters"), false, "the cache owns the presentation model, not the transport DTO");
  await state.loadCampaignContext({ envelope });
  await state.loadCampaignContext({ envelope });
  assert.equal(detailReads, 1);
  assert.equal(contextReads, 1);
  assert.equal(state.invalidateObject(CAMPAIGN_LOCATION_VISITS_OBJECT_ID), true);
  await state.loadCampaignDetails({ envelope });
  await state.loadCampaignContext({ envelope });
  assert.equal(detailReads, 2);
  assert.equal(contextReads, 1);
  assert.equal(state.invalidateObject(WORLD_CAMPAIGN_DIRECTORY_OBJECT_ID), true);
  await state.loadCampaignContext({ envelope });
  assert.equal(contextReads, 2);
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

test("scope replacement retires prior Campaign resources and cache expiry remains bounded", async () => {
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
  assert.equal(state.peekCampaign({ perspective: "dm", campaignId: "one" }), null);
  assert.equal(state.peekCampaign({ perspective: "dm", campaignId: "two" }), null);
  assert.notEqual(state.peekCampaign({ perspective: "dm", campaignId: "three" }), null);

  const expiring = new TableResourceOwner({
    maximumAgeMs: 0,
    readCampaign: async ({ perspective, campaignId }) => campaign(perspective, campaignId ?? "bound"),
    readFactionPage: async () => page(),
    readCampaignDetails: async () => { throw new Error("not used"); },
    readCampaignContext: async () => { throw new Error("not used"); },
    validateCampaign: isCampaign,
  });
  await expiring.loadCampaign({ perspective: "player", campaignId: "one" });
  assert.equal(expiring.peekCampaign({ perspective: "player", campaignId: "one" }), null);
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

test("Character sheet, detail, and inventory resources deduplicate independently and invalidate transfers", async () => {
  let sheetReads = 0;
  let detailReads = 0;
  let inventoryReads = 0;
  const owner = new CharacterResourceOwner({
    readSheet: async ({ actorId }) => { sheetReads += 1; return character(actorId); },
    readDetails: async ({ actorId }) => { detailReads += 1; return character(actorId); },
    readInventory: async ({ actorId }) => { inventoryReads += 1; return inventory(actorId); },
  });
  const envelope = scope();
  const first = { envelope, actorId: "actor.first" };
  await Promise.all([owner.loadSheet(first), owner.loadSheet(first)]);
  await owner.loadDetails(first);
  await Promise.all([owner.loadInventory(first), owner.loadInventory(first)]);
  await owner.loadSheet(first);
  assert.equal(sheetReads, 1);
  assert.equal(detailReads, 1);
  assert.equal(inventoryReads, 1);

  assert.equal(owner.invalidateObject("dnd2024.object.inventory-item-fixture"), true);
  await owner.loadSheet(first);
  await owner.loadDetails(first);
  await owner.loadInventory(first);
  assert.equal(sheetReads, 1, "an inventory transfer keeps the sheet cache");
  assert.equal(detailReads, 2, "an inventory transfer retires dossier-derived detail");
  assert.equal(inventoryReads, 2, "an inventory transfer retires the container projection");

  await owner.loadSheet({ envelope, actorId: "actor.second" });
  await owner.loadSheet(first);
  assert.equal(sheetReads, 2, "switching back reuses the first character sheet");

  assert.equal(owner.invalidateObject(CHARACTER_DOSSIER_OBJECT_ID), true);
  await owner.loadSheet(first);
  await owner.loadDetails(first);
  assert.equal(sheetReads, 3);
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

test("result revisions do not fragment Character cache keys but resolution changes do", async () => {
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
  assert.equal(reads, 1, "source/result evidence belongs with results, not resource identity");
  await owner.loadSheet({ envelope: scopeWithCampaignEvidence("C".repeat(64), "6".repeat(64), "7".repeat(64)),
    actorId: "actor.first" });
  assert.equal(reads, 2, "effective query resolution fences incompatible cached results");
  assert.ok(owner.cacheMetrics().retainedBytes > 0);
});

test("World scope resources deduplicate exact locations and fence audience changes", async () => {
  let reads = 0;
  const owner = new WorldResourceOwner({
    readScope: async ({ scopeId }) => {
      reads += 1;
      return {
        section: "locations" as const,
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
  assert.equal(reads, 1);
  await owner.loadScope({ envelope: dm, scopeId: "location.region" });
  assert.equal(reads, 2, "a nested map scope is a separate bounded resource");
  await owner.loadScope({ envelope: scope("player"), scopeId: "world.fixture" });
  await owner.loadScope({ envelope: dm, scopeId: "world.fixture" });
  assert.equal(reads, 4, "audience replacement retires every prior-scope map resource");
});

test("World first-page cache identity ignores a newly materialized result revision", async () => {
  let reads = 0;
  const owner = new WorldResourceOwner({
    readScope: async ({ scopeId }) => {
      reads += 1;
      return { section: "locations", world: { currentLocationId: "", map: { imageUrl: "", alt: "Map unavailable" },
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
  assert.equal(reads, 1);
  assert.equal(owner.cacheMetrics().hits, 1);
});

test("World information resources cache People, Lore, and History independently and fence observers", async () => {
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
  assert.deepEqual(reads, { people: 1, lore: 1, history: 1 });

  const actor = scope("player");
  await owner.loadInformation({ envelope: actor, section: "people" });
  await owner.loadInformation({ envelope: dm, section: "people" });
  assert.equal(reads.people, 3, "observer replacement retires cached private information");
});

test("World People resources accept the declared combined 200-record projection bound", async () => {
  const update = (people: number) => ({
    section: "people" as const,
    world: {
      locations: Array.from({ length: 15 }, (_, index) => ({ id: `location-${index}` })),
      people: Array.from({ length: people }, (_, index) => ({ id: `person-${index}` })),
    },
  }) as unknown as import("../../src/data/object-resources").WorldInformationUpdate;
  const owner = new WorldResourceOwner({
    readScope: async () => ({}) as import("../../src/data/object-resources").WorldScopeUpdate,
    readInformation: async () => update(185),
  });

  const result = await owner.loadInformation({ envelope: scope("dm"), section: "people" });
  assert.equal(result.world.locations.length + result.world.people.length, 200);

  const overflow = new WorldResourceOwner({
    readScope: async () => ({}) as import("../../src/data/object-resources").WorldScopeUpdate,
    readInformation: async () => update(186),
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
    world: { currentLocationId: locationId, locations: [] },
    campaign: { mapOverlays: [] },
  });
  const owner = new CurrentViewResourceOwner({
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
  assert.equal((await player).world.currentLocationId, "location.player");
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
  assert.equal((await refreshed).world.currentLocationId, "location.changed");
  assert.equal(reads, 3);
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
