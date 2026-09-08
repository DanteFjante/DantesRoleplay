import assert from "node:assert/strict";
import test from "node:test";

import {
  CharacterResourceOwner,
  CHARACTER_DOSSIER_OBJECT_ID,
  TableResourceOwner,
  CAMPAIGN_SUMMARY_OBJECT_ID,
  CAMPAIGN_LOCATION_VISITS_OBJECT_ID,
  FACTION_DIRECTORY_OBJECT_ID,
  WORLD_CAMPAIGN_DIRECTORY_OBJECT_ID,
  type FactionDirectoryPage,
} from "../../src/data/object-resources";
import { createHubObjectUiState, hubObjectUiReducer } from "../../src/data/hub-object-ui";
import type { CampaignReadModel, HubEnvelope, PartyMemberReadModel, Perspective, ReadyHubEnvelope } from "../../src/data/hub-types";
import { ViewReadError } from "../../src/data/view-read-client";

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
      factionDirectory: sourceRevisionFingerprint === null ? undefined : {
        totalCount: 2, complete: false, nextCursor: "next", sourceRevisionFingerprint,
      },
    },
  } as unknown as ReadyHubEnvelope;
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

test("Campaign details and context use independent bounded shared resources", async () => {
  let detailReads = 0;
  let contextReads = 0;
  const envelope = scope();
  const details = {
    id: "campaign.fixture", title: "Fixture", chapters: [], arcs: [], sessions: [], visitedLocations: [],
  } as unknown as CampaignReadModel;
  const context = { section: "context" as const, contextSelection: {
    selectedCampaignId: "campaign.fixture", selectedWorldId: "world.fixture",
    worlds: [{ id: "world.fixture", name: "Fixture", campaigns: [{ id: "campaign.fixture", name: "Fixture" }] }],
  } };
  const state = new TableResourceOwner({
    readCampaign: async ({ perspective, campaignId }) => campaign(perspective, campaignId ?? "bound"),
    readFactionPage: async () => page(),
    readCampaignDetails: async () => { detailReads += 1; return details; },
    readCampaignContext: async () => { contextReads += 1; return context; },
    validateCampaign: isCampaign,
  });
  await state.loadCampaignDetails({ envelope });
  await state.loadCampaignDetails({ envelope });
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

test("Character sheet and detail resources deduplicate independently and reuse same-character cache", async () => {
  let sheetReads = 0;
  let detailReads = 0;
  const owner = new CharacterResourceOwner({
    readSheet: async ({ actorId }) => { sheetReads += 1; return character(actorId); },
    readDetails: async ({ actorId }) => { detailReads += 1; return character(actorId); },
  });
  const envelope = scope();
  const first = { envelope, actorId: "actor.first" };
  await Promise.all([owner.loadSheet(first), owner.loadSheet(first)]);
  await owner.loadDetails(first);
  await owner.loadSheet(first);
  assert.equal(sheetReads, 1);
  assert.equal(detailReads, 1);

  await owner.loadSheet({ envelope, actorId: "actor.second" });
  await owner.loadSheet(first);
  assert.equal(sheetReads, 2, "switching back reuses the first character sheet");

  assert.equal(owner.invalidateObject(CHARACTER_DOSSIER_OBJECT_ID), true);
  await owner.loadSheet(first);
  await owner.loadDetails(first);
  assert.equal(sheetReads, 3);
  assert.equal(detailReads, 2);
});

test("Character scope replacement fences cached actors across perspectives", async () => {
  let reads = 0;
  const owner = new CharacterResourceOwner({
    readSheet: async ({ actorId }) => { reads += 1; return character(actorId); },
    readDetails: async ({ actorId }) => character(actorId),
  });
  await owner.loadSheet({ envelope: scope("dm"), actorId: "actor.first" });
  await owner.loadSheet({ envelope: scope("player"), actorId: "actor.first" });
  await owner.loadSheet({ envelope: scope("dm"), actorId: "actor.first" });
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

  state = hubObjectUiReducer(state, { type: "write-confirmed", objectId: CAMPAIGN_SUMMARY_OBJECT_ID });
  assert.equal(state.edits[CAMPAIGN_SUMMARY_OBJECT_ID], undefined);
  state = hubObjectUiReducer(state, { type: "scope-replaced", factionId: "faction.two" });
  assert.deepEqual(state, createHubObjectUiState("faction.two"));
});
