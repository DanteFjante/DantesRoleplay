import assert from "node:assert/strict";
import test from "node:test";

import { JSDOM } from "jsdom";
import React, { act } from "react";
import { Provider } from "react-redux";

import { ItemWorkspace } from "../../src/components/items/ItemWorkspace";
import { CharacterResourceOwner } from "../../src/data/object-resources";
import {
  characterScope,
  commitCurrent,
  commitCharacterFacet,
  commitInventory,
  currentActions,
  currentScope,
  createHubStore,
  hubActions,
  peekCharacterFacet,
  peekCurrentDisplay,
  selectCharacterParty,
  selectCharacterMember,
  selectCurrentDisplay,
  selectCurrentFresh,
  selectTableEnvelope,
  tableActions,
  tableFacetFresh,
  tableScope,
  commitDeferredTable,
  commitDeferredTableProgress,
  commitCampaignDetails,
  commitFactionPage,
  MAX_CONFIRMED_TABLE_BYTES,
  useHubSelector,
} from "../../src/data/hub-store";
import type { InventoryContainerPageResult, InventoryContainerResult, PartyMemberReadModel, ReadyHubEnvelope } from "../../src/data/hub-types";
import { allocateTableRequestToken } from "../../src/data/table-store";

function envelope(perspective: "dm" | "player" = "dm") {
  return {
    applicationId: "dnd2024", stateSpaceId: "state.fixture", revision: "campaign.fixture",
    audience: { seat: "dm", perspective },
    contextSelection: { selectedCampaignId: "campaign.fixture", selectedWorldId: "world.fixture" },
    objectQueries: { campaignSummary: { resolutionFingerprint: "A".repeat(64) } },
  } as unknown as ReadyHubEnvelope;
}

function member(portrait?: unknown) {
  const result = {
    id: "actor.fixture", name: "Ganji", detail: [], sheet: [], inventory: [], initials: "G",
    recordStatus: "ready", sheetStatus: "ready", inventoryStatus: "ready",
    sheetState: { status: "ready" }, inventoryState: { status: "ready" },
  } as unknown as PartyMemberReadModel;
  if (portrait !== undefined) Object.assign(result, { portrait });
  return result;
}

function readyTableEnvelope(): ReadyHubEnvelope {
  const actor = member(); Object.assign(actor, { isCurrent: true });
  return {
    version: 1, status: "ready", applicationId: "dnd2024", stateSpaceId: "state.fixture",
    revision: "campaign.fixture",
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    contextSelection: { selectedCampaignId: "campaign.fixture", selectedWorldId: "world.fixture", worlds: [] },
    world: {
      id: "world.fixture", name: "Fixture World", era: "Now", summary: "World", premise: "Premise",
      currentLocationId: "location.one", map: { imageUrl: "", alt: "Map unavailable" }, mapOwnerId: null,
      rootMapId: "map.synthetic", maps: [{ id: "map.synthetic", scope: "world", parentMapId: null,
        subject: { kind: "World", id: "world.fixture", name: "Fixture World" },
        coordinateSpace: { id: "space", unit: "normalized", width: 1, height: 1 }, baseState: "absent",
        base: null, layers: [], features: [], scopeLinks: [] }],
      regions: [], facts: [], historyCoverage: "partial", history: [{ id: "history.old", sortOrder: null, date: "Now", era: "Now",
        title: "Old", category: "event", region: "Fixture", status: "known", summary: "Old",
        linkedLocations: [], linkedPeople: [] }],
      locations: [{ id: "location.one", name: "One", region: "Fixture", kind: "site", status: "known",
        summary: "One", description: "One", atmosphere: "", landmarks: [], observations: [], routes: [],
        mapAnchor: null, people: [], unavailableFields: ["mapAnchor"] }],
      locationScopes: [{ id: "world.fixture", name: "Fixture World", parentId: null,
        childIds: ["location.one"], totalCount: 1, complete: true, nextCursor: null,
        sourceRevisionFingerprint: "A".repeat(64), coverage: "partial" }],
      people: [], factions: [], lore: [],
    },
    campaign: {
      title: "Fixture Campaign", subtitle: "Campaign", status: "Active", chapter: "The Thirteenth Bell",
      question: "What next?", premise: "Premise", progress: "In progress", objective: "Continue", stakes: "High",
      nextMilestone: "Next", facts: [], adventureLog: [], placesVisited: [], outcomes: [], mapOverlays: [],
      quests: [], threads: [], clues: [],
    },
    party: [actor], rules: [], objectQueries: { campaignSummary: { resolutionFingerprint: "A".repeat(64) } as never },
  };
}

test("Campaign, World, Map, History, Lore, and directory records have one normalized Redux owner", () => {
  const store = createHubStore();
  const ready = readyTableEnvelope();
  store.dispatch(tableActions.bootstrapCommitted({ scope: tableScope(ready), envelope: ready }));
  store.dispatch(hubActions.bootstrapCommitted({ scope: characterScope(ready), party: ready.party }));
  const selected = selectTableEnvelope(store.getState())!;
  assert.equal(selected.campaign.chapter, "The Thirteenth Bell");
  assert.equal(selected.world.locations[0]?.id, "location.one");
  assert.equal(store.getState().table.world?.locations.byId["location.one"]?.name, "One");
  assert.equal(store.getState().table.world?.history.byId["history.old"]?.title, "Old");
  assert.equal(selected.world.historyCoverage, "partial");
  assert.equal(selected.world.history[0]?.sortOrder, null);
  assert.equal(selected.world.locationScopes[0]?.coverage, "partial");
  assert.equal(selected.world.locations[0]?.mapAnchor, null);
  assert.equal("envelope" in store.getState().table, false,
    "the Redux slice stores normalized records rather than a parallel response envelope");
});

test("ambiguous normalized identities reject the whole commit without claiming complete coverage", () => {
  const store = createHubStore();
  const ready = readyTableEnvelope();
  const scope = tableScope(ready);
  store.dispatch(tableActions.bootstrapCommitted({ scope, envelope: ready }));
  const before = selectTableEnvelope(store.getState());
  const duplicate = {
    ...ready,
    world: { ...ready.world, locations: [ready.world.locations[0]!, ready.world.locations[0]!] },
  };
  store.dispatch(tableActions.bootstrapCommitted({ scope, envelope: duplicate }));
  assert.equal(selectTableEnvelope(store.getState()), before,
    "a duplicate identity cannot partially replace a complete normalized collection");
});

test("Table facets enforce aggregate bytes and freshness without renewing confirmed evidence", () => {
  const store = createHubStore();
  const ready = readyTableEnvelope();
  const scope = tableScope(ready);
  store.dispatch(tableActions.bootstrapCommitted({ scope, envelope: ready }));
  const generation = store.getState().table.generation;
  const confirmedAt = Date.now();
  store.dispatch(tableActions.requestStarted({ scope, key: "campaign-details", requestToken: 91 }));
  store.dispatch(commitCampaignDetails({ scope, key: "campaign-details", value: ready.campaign }, {
    generation, requestToken: 91, bytes: 100, confirmedAt,
  }));
  assert.equal(tableFacetFresh(store.getState().table, scope, "campaign-details", 30_000), true);
  assert.equal(store.getState().table.facets["campaign-details"]?.confirmedAt, confirmedAt);

  const before = selectTableEnvelope(store.getState());
  store.dispatch(tableActions.requestStarted({ scope, key: "campaign-details", requestToken: 92 }));
  store.dispatch(commitCampaignDetails({ scope, key: "campaign-details", value: {
    ...ready.campaign, chapter: "x".repeat(MAX_CONFIRMED_TABLE_BYTES - 1_000),
  } }, { generation, requestToken: 92, bytes: MAX_CONFIRMED_TABLE_BYTES - 500, confirmedAt: confirmedAt + 1 }));
  assert.deepEqual(selectTableEnvelope(store.getState()), before,
    "a facet whose normalized aggregate exceeds the cap is rejected atomically");
  store.dispatch(tableActions.invalidated());
  assert.equal(tableFacetFresh(store.getState().table, scope, "campaign-details", Number.MAX_SAFE_INTEGER), false);
});

test("a late Campaign details read cannot replace independently confirmed knowledge or map facets", () => {
  const store = createHubStore();
  const ready = readyTableEnvelope();
  const scope = tableScope(ready);
  ready.campaign.cluesCoverage = "unavailable";
  store.dispatch(tableActions.bootstrapCommitted({ scope, envelope: ready }));
  const generation = store.getState().table.generation;
  store.dispatch(tableActions.requestStarted({ scope, key: "campaign-details", requestToken: 95 }));
  store.dispatch(tableActions.requestStarted({ scope, key: "deferred:lore", requestToken: 96 }));
  const knowledge = { quests: [], clues: [], cluesCoverage: "partial" as const, mapOverlays: [] };
  store.dispatch(commitDeferredTable({ scope, key: "deferred:lore", value: {
    section: "lore", world: { lore: [] }, campaign: knowledge,
  } }, { generation, requestToken: 96, bytes: 100, confirmedAt: 20 }));
  store.dispatch(commitCampaignDetails({ scope, key: "campaign-details", value: {
    ...ready.campaign, chapter: "New chapter retained", cluesCoverage: "unavailable",
    quests: [{ id: "placeholder.quest" }] as typeof ready.campaign.quests,
    clues: [{ id: "placeholder.clue" }] as typeof ready.campaign.clues,
    mapOverlays: [{ id: "placeholder.overlay" }] as typeof ready.campaign.mapOverlays,
  } }, { generation, requestToken: 95, bytes: 100, confirmedAt: 21 }));
  const campaign = selectTableEnvelope(store.getState())!.campaign;
  assert.equal(campaign.chapter, "New chapter retained");
  assert.equal(campaign.cluesCoverage, "partial");
  assert.deepEqual(campaign.clues, []);
  assert.deepEqual(campaign.quests, []);
  assert.deepEqual(campaign.mapOverlays, []);
  assert.equal(store.getState().table.facets["deferred:lore"]!.confirmedAt, 20);
});

test("source-fenced deferred progress retains its one request through prefixes and fences a late scope", () => {
  const store = createHubStore();
  const ready = readyTableEnvelope();
  ready.campaign.cluesCoverage = "unavailable";
  const scope = tableScope(ready);
  store.dispatch(tableActions.bootstrapCommitted({ scope, envelope: ready }));
  const generation = store.getState().table.generation;
  const requestToken = allocateTableRequestToken();
  const key = "deferred:lore";
  store.dispatch(tableActions.requestStarted({ scope, key, requestToken }));
  const prefix = (ids: string[], coverage: "partial" | "complete") => ({
    section: "lore" as const, world: { lore: [] }, campaign: {
      quests: [], clues: ids.map((id) => ({ id })) as typeof ready.campaign.clues,
      cluesCoverage: coverage, mapOverlays: [],
    },
  });
  store.dispatch(commitDeferredTableProgress({ scope, key, value: prefix(["clue.one"], "partial") }, {
    generation, requestToken, bytes: 100, confirmedAt: 10,
  }));
  assert.deepEqual(selectTableEnvelope(store.getState())!.campaign.clues.map((clue) => clue.id), ["clue.one"]);
  assert.equal(store.getState().table.requests[key]?.requestToken, requestToken,
    "a prefix must retain the continuation lease");
  store.dispatch(commitDeferredTableProgress({ scope, key, value: prefix(["clue.one", "clue.two"], "partial") }, {
    generation, requestToken, bytes: 120, confirmedAt: 11,
  }));
  assert.deepEqual(selectTableEnvelope(store.getState())!.campaign.clues.map((clue) => clue.id), ["clue.one", "clue.two"]);
  store.dispatch(commitDeferredTable({ scope, key, value: prefix(["clue.one", "clue.two", "clue.three"], "complete") }, {
    generation, requestToken, bytes: 140, confirmedAt: 12,
  }));
  assert.deepEqual(selectTableEnvelope(store.getState())!.campaign.clues.map((clue) => clue.id), ["clue.one", "clue.two", "clue.three"]);
  assert.equal(store.getState().table.requests[key], undefined, "only the terminal commit retires the lease");

  const lateToken = allocateTableRequestToken();
  store.dispatch(tableActions.requestStarted({ scope, key, requestToken: lateToken }));
  store.dispatch(tableActions.invalidated());
  store.dispatch(commitDeferredTableProgress({ scope, key, value: prefix(["clue.late"], "partial") }, {
    generation, requestToken: lateToken, bytes: 100, confirmedAt: 13,
  }));
  assert.deepEqual(selectTableEnvelope(store.getState())!.campaign.clues.map((clue) => clue.id), ["clue.one", "clue.two", "clue.three"],
    "a late progress callback cannot mutate the invalidated generation");
});

test("late World completion cannot overwrite a newer invalidation generation", () => {
  const store = createHubStore();
  const ready = readyTableEnvelope();
  const scope = tableScope(ready);
  store.dispatch(tableActions.bootstrapCommitted({ scope, envelope: ready }));
  const generation = store.getState().table.generation;
  store.dispatch(tableActions.requestStarted({ scope, key: "deferred:history", requestToken: 101 }));
  store.dispatch(tableActions.invalidated());
  store.dispatch(commitDeferredTable({ scope, key: "deferred:history", value: {
    section: "history", world: { history: [{ ...ready.world.history[0]!, id: "history.late", title: "Late" }] },
  } }, { generation, requestToken: 101, bytes: 100, confirmedAt: 10 }));
  assert.deepEqual(selectTableEnvelope(store.getState())?.world.history.map((entry) => entry.id), ["history.old"]);
});

test("same-scope bootstrap cannot retain fresh facet claims over unloaded replacement bodies", () => {
  const store = createHubStore();
  const ready = readyTableEnvelope();
  ready.campaign.cluesCoverage = "unavailable";
  const scope = tableScope(ready);
  store.dispatch(tableActions.bootstrapCommitted({ scope, envelope: ready }));
  const generation = store.getState().table.generation;
  const requestToken = allocateTableRequestToken();
  store.dispatch(tableActions.requestStarted({ scope, key: "deferred:lore", requestToken }));
  store.dispatch(commitDeferredTable({ scope, key: "deferred:lore", value: {
    section: "lore", world: { lore: [] },
    campaign: { quests: [], clues: [], cluesCoverage: "complete", mapOverlays: [] },
  } }, { generation, requestToken, bytes: 100, confirmedAt: Date.now() }));
  assert.equal(selectTableEnvelope(store.getState())!.campaign.cluesCoverage, "complete");
  assert.equal(tableFacetFresh(store.getState().table, scope, "deferred:lore", 30_000), true);
  const oldRequest = allocateTableRequestToken();
  store.dispatch(tableActions.requestStarted({ scope, key: "deferred:history", requestToken: oldRequest }));
  store.dispatch(tableActions.bootstrapCommitted({ scope, envelope: ready }));
  assert.equal(selectTableEnvelope(store.getState())!.campaign.cluesCoverage, "unavailable");
  assert.equal(tableFacetFresh(store.getState().table, scope, "deferred:lore", 30_000), false,
    "the missing clue collection must be fetched again, not treated as a retained response");
  assert.ok(store.getState().table.generation > generation);
  store.dispatch(commitDeferredTable({ scope, key: "deferred:history", value: {
    section: "history", world: { history: [{ ...ready.world.history[0]!, id: "history.late" }] },
  } }, { generation, requestToken: oldRequest, bytes: 100, confirmedAt: Date.now() }));
  assert.equal(selectTableEnvelope(store.getState())!.world.history[0]!.id, "history.old");
});

test("Faction coverage survives continuations and resets only on a fresh first page", () => {
  const store = createHubStore();
  const ready = readyTableEnvelope();
  const scope = tableScope(ready);
  store.dispatch(tableActions.bootstrapCommitted({ scope, envelope: ready }));
  const generation = store.getState().table.generation;
  const commit = (cursor: string | null, coverage: "partial" | "complete", requestToken: number) => {
    const key = `factions:${cursor ?? "root"}`;
    store.dispatch(tableActions.requestStarted({ scope, key, requestToken }));
    store.dispatch(commitFactionPage({ scope, key, cursor, value: {
      factions: [], totalCount: 0, complete: true, nextCursor: null, coverage,
      sourceRevisionFingerprint: "A".repeat(64),
    } }, { generation, requestToken, bytes: 100, confirmedAt: Date.now() }));
    return selectTableEnvelope(store.getState())!.world.factionDirectory!.coverage;
  };
  assert.equal(commit(null, "partial", 121), "partial");
  assert.equal(commit("next", "complete", 122), "partial");
  assert.equal(commit(null, "complete", 123), "complete");
});

test("recoverable Current invalidation preserves a stale display and excludes it from fresh cache use", () => {
  const store = createHubStore();
  const ready = readyTableEnvelope();
  const scope = currentScope(ready);
  const display = { situation: { status: "unavailable", message: "Last scene" }, location: null } as const;
  store.dispatch(currentActions.scopeReplaced({ scope }));
  store.dispatch(currentActions.currentRequestStarted({ scope, requestToken: 201 }));
  store.dispatch(commitCurrent({ scope, value: display }, {
    generation: store.getState().current.generation, requestToken: 201, confirmedAt: Date.now(), bytes: 80,
  }));
  store.dispatch(currentActions.invalidated());
  assert.deepEqual(selectCurrentDisplay(scope)(store.getState()), display);
  assert.equal(selectCurrentFresh(scope)(store.getState()), false);
  assert.equal(peekCurrentDisplay(store.getState(), scope, Number.MAX_SAFE_INTEGER), null);
});

test("confirmed character selectors preserve an omitted portrait but clear it at a changed scope", () => {
  const store = createHubStore();
  const first = envelope();
  const firstScope = characterScope(first);
  const portrait = { imageUrl: "/portrait/confirmed", alt: "Ganji", width: 100, height: 150 };
  store.dispatch(hubActions.bootstrapCommitted({ scope: firstScope, party: [member(portrait)] }));
  const initial = selectCharacterParty(store.getState());
  assert.equal(selectCharacterParty(store.getState()), initial, "selectors are stable without a state change");

  store.dispatch(hubActions.bootstrapCommitted({ scope: firstScope, party: [member()] }));
  assert.deepEqual(selectCharacterParty(store.getState())[0]?.portrait, portrait);

  const other = envelope("player");
  store.dispatch(hubActions.bootstrapCommitted({ scope: characterScope(other), party: [member()] }));
  assert.equal(selectCharacterParty(store.getState())[0]?.portrait, undefined,
    "a new private scope never inherits a prior scope portrait");
});

test("a changed player actor binding is a private character scope even when campaign and perspective match", () => {
  const store = createHubStore();
  const playerA = member({ imageUrl: "/portrait/a", alt: "A", width: 1, height: 1 });
  Object.assign(playerA, { isCurrent: true });
  const playerB = member(); Object.assign(playerB, { id: "actor.player-b", isCurrent: true });
  const first = { ...envelope("player"), party: [playerA] };
  const second = { ...envelope("player"), party: [playerB] };
  store.dispatch(hubActions.bootstrapCommitted({ scope: characterScope(first), party: first.party }));
  store.dispatch(hubActions.bootstrapCommitted({ scope: characterScope(second), party: second.party }));
  assert.deepEqual(selectCharacterParty(store.getState()).map((value) => value.id), ["actor.player-b"]);
  assert.equal(selectCharacterParty(store.getState())[0]?.portrait, undefined);
});

test("Current uses the complete private character scope and rejects a late prior-scope commit", () => {
  const store = createHubStore();
  const playerA = { ...envelope("player"), party: [{ id: "actor.a", isCurrent: true }] } as ReadyHubEnvelope;
  const playerB = { ...envelope("player"), party: [{ id: "actor.b", isCurrent: true }] } as ReadyHubEnvelope;
  const firstScope = currentScope(playerA);
  const secondScope = currentScope(playerB);
  assert.notEqual(firstScope, secondScope, "the active actor binding is part of Current scope");
  const display = { situation: { status: "unavailable", message: "No scene." }, location: null } as const;
  store.dispatch(currentActions.scopeReplaced({ scope: firstScope }));
  store.dispatch(currentActions.currentRequestStarted({ scope: firstScope, requestToken: 11 }));
  store.dispatch(commitCurrent({ scope: firstScope, value: display },
    { generation: store.getState().current.generation, requestToken: 11, confirmedAt: 10, bytes: 80 }));
  assert.deepEqual(selectCurrentDisplay(firstScope)(store.getState()), display);
  const oldGeneration = store.getState().current.generation;
  store.dispatch(currentActions.scopeReplaced({ scope: secondScope }));
  store.dispatch(commitCurrent({ scope: firstScope, value: display },
    { generation: oldGeneration, requestToken: 11, confirmedAt: 20, bytes: 80 }));
  assert.equal(selectCurrentDisplay(secondScope)(store.getState()), null,
    "a late former-player result cannot refill the replacement scope");
});

test("a failed Current refresh keeps the display but never renews its cache freshness", () => {
  const store = createHubStore();
  const current = envelope();
  const scope = currentScope(current);
  const display = { situation: { status: "unavailable", message: "No scene." }, location: null } as const;
  store.dispatch(currentActions.scopeReplaced({ scope }));
  store.dispatch(currentActions.currentRequestStarted({ scope, requestToken: 1 }));
  store.dispatch(commitCurrent({ scope, value: display },
    { generation: store.getState().current.generation, requestToken: 1, confirmedAt: Date.now(), bytes: 80 }));
  assert.deepEqual(peekCurrentDisplay(store.getState(), scope, 1_000), display);
  store.dispatch(currentActions.currentRequestStarted({ scope, requestToken: 2 }));
  store.dispatch(currentActions.currentRequestFinished({ scope, requestToken: 2 }));
  assert.deepEqual(selectCurrentDisplay(scope)(store.getState()), display,
    "a temporary failure keeps the last confirmed Current scene visible");
  assert.equal(peekCurrentDisplay(store.getState(), scope, 1_000), null,
    "the retained scene is not a fresh cache hit after that failure");
});

test("newer explicit facets revoke old portrait fields and leave unrelated member references stable", () => {
  const store = createHubStore();
  const current = envelope();
  const scope = characterScope(current);
  const first = member({ imageUrl: "/portrait/one", alt: "one", width: 1, height: 1 });
  const second = member(); second.id = "actor.second";
  store.dispatch(hubActions.bootstrapCommitted({ scope, party: [first, second] }));
  const before = selectCharacterParty(store.getState());
  const firstSelector = selectCharacterMember(first.id);
  const selectedBefore = firstSelector(store.getState());
  const details = member({ imageUrl: "/portrait/details", alt: "details", width: 1, height: 1 });
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: first.id, facet: "details", requestToken: 1 }));
  store.dispatch(commitCharacterFacet({ scope, actorId: first.id, facet: "details", value: details },
    { confirmedAt: 10, generation: 1, requestToken: 1, bytes: 100 }));
  const sheet = member(); Object.assign(sheet, { portrait: undefined, portraitCoverage: "denied" });
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: first.id, facet: "sheet", requestToken: 2 }));
  store.dispatch(commitCharacterFacet({ scope, actorId: first.id, facet: "sheet", value: sheet },
    { confirmedAt: 11, generation: 1, requestToken: 2, bytes: 100 }));
  const after = selectCharacterParty(store.getState());
  assert.equal(after[0]?.portrait, undefined, "a newer explicit sheet revocation beats older details");
  assert.equal(after[1], before[1], "a facet for one actor does not recreate unrelated actor values");
  assert.equal(firstSelector(store.getState()), after[0], "actor selectors memoize the composed record");
  assert.notEqual(firstSelector(store.getState()), selectedBefore, "the selected actor changes only for its own facet");
});

test("roster admission rejects malformed prototype-like identities without losing safe own keys", () => {
  const store = createHubStore();
  const current = envelope();
  const safe = member(); safe.id = "constructor";
  const unsafe = member(); unsafe.id = "__proto__";
  const malformed = member(); malformed.id = "a/b";
  store.dispatch(hubActions.bootstrapCommitted({ scope: characterScope(current), party: [safe, unsafe, malformed] }));
  assert.deepEqual(selectCharacterParty(store.getState()).map((value) => value.id), ["constructor"]);
  const scope = characterScope(current);
  const updated = member(); updated.id = "constructor"; updated.detail = "Own-key facet";
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: "constructor", facet: "sheet", requestToken: 1 }));
  store.dispatch(commitCharacterFacet({ scope, actorId: "constructor", facet: "sheet", value: updated },
    { generation: 1, requestToken: 1, confirmedAt: 1, bytes: 100 }));
  assert.equal(selectCharacterParty(store.getState())[0]?.detail, "Own-key facet");
});

test("request-start tokens fence late completions and portrait coverage distinguishes unavailable from denial", () => {
  const store = createHubStore();
  const current = envelope();
  const scope = characterScope(current);
  const base = member({ imageUrl: "/portrait/base", alt: "base", width: 1, height: 1 });
  store.dispatch(hubActions.bootstrapCommitted({ scope, party: [base] }));
  const unavailable = member({ imageUrl: "/copied-summary", alt: "copied", width: 1, height: 1 });
  unavailable.portraitCoverage = "unavailable";
  const denied = member({ imageUrl: "/portrait/should-not-render", alt: "stale", width: 1, height: 1 });
  denied.portraitCoverage = "denied";
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: base.id, facet: "sheet", requestToken: 1 }));
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: base.id, facet: "sheet", requestToken: 2 }));
  store.dispatch(commitCharacterFacet({ scope, actorId: base.id, facet: "sheet", value: unavailable },
    { generation: 1, requestToken: 1, confirmedAt: 1, bytes: 100 }));
  assert.equal(selectCharacterParty(store.getState())[0]?.portrait?.imageUrl, "/portrait/base",
    "the replaced request cannot install a copied stale portrait");
  store.dispatch(commitCharacterFacet({ scope, actorId: base.id, facet: "sheet", value: unavailable },
    { generation: 1, requestToken: 2, confirmedAt: 2, bytes: 100 }));
  assert.equal(selectCharacterParty(store.getState())[0]?.portrait?.imageUrl, "/portrait/base",
    "unavailable media has no portrait coverage");
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: base.id, facet: "details", requestToken: 3 }));
  store.dispatch(commitCharacterFacet({ scope, actorId: base.id, facet: "details", value: denied },
    { generation: 1, requestToken: 3, confirmedAt: 3, bytes: 100 }));
  assert.equal(selectCharacterParty(store.getState())[0]?.portrait, undefined,
    "explicitly denied media clears the old portrait");
  store.dispatch(hubActions.characterFacetsCleared(undefined));
  store.dispatch(commitCharacterFacet({ scope, actorId: base.id, facet: "sheet", value: unavailable },
    { generation: 1, requestToken: 2, confirmedAt: 4, bytes: 100 }));
  assert.equal(store.getState().confirmed.retainedBytes, 0, "an invalidated generation cannot revive a facet");
});

test("inventory-only completion is composed without a sheet or details facet", () => {
  const store = createHubStore();
  const current = envelope();
  const scope = characterScope(current);
  const actor = member();
  const inventory = { status: "ready", data: { container: { id: actor.id }, items: [] } } as InventoryContainerResult;
  store.dispatch(hubActions.bootstrapCommitted({ scope, party: [actor] }));
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "inventory", requestToken: 1 }));
  store.dispatch(commitInventory({ scope, actorId: actor.id, value: inventory },
    { generation: 1, requestToken: 1, confirmedAt: 1, bytes: 100 }));
  assert.equal(selectCharacterParty(store.getState())[0]?.inventoryResource, inventory);
});

test("unavailable refresh preserves prior portrait authority and eviction cannot revive base media", () => {
  const store = createHubStore();
  const current = envelope();
  const scope = characterScope(current);
  const actor = member({ imageUrl: "/portrait/base", alt: "base", width: 1, height: 1 });
  store.dispatch(hubActions.bootstrapCommitted({ scope, party: [actor] }));
  const fresh = member({ imageUrl: "/portrait/fresh", alt: "fresh", width: 1, height: 1 });
  fresh.portraitCoverage = "confirmed";
  const unavailable = member({ imageUrl: "/portrait/copied", alt: "copied", width: 1, height: 1 });
  unavailable.portraitCoverage = "unavailable";
  const denied = member(); Object.assign(denied, { portrait: undefined, portraitCoverage: "denied" });
  for (const [token, value] of [[1, fresh], [2, unavailable]] as const) {
    store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "sheet", requestToken: token }));
    store.dispatch(commitCharacterFacet({ scope, actorId: actor.id, facet: "sheet", value },
      { generation: 1, requestToken: token, confirmedAt: token, bytes: 100 }));
  }
  assert.equal(selectCharacterParty(store.getState())[0]?.portrait?.imageUrl, "/portrait/fresh");
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "sheet", requestToken: 3 }));
  store.dispatch(commitCharacterFacet({ scope, actorId: actor.id, facet: "sheet", value: denied },
    { generation: 1, requestToken: 3, confirmedAt: 3, bytes: 100 }));
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "sheet", requestToken: 4 }));
  store.dispatch(commitCharacterFacet({ scope, actorId: actor.id, facet: "sheet", value: unavailable },
    { generation: 1, requestToken: 4, confirmedAt: 4, bytes: 100 }));
  assert.equal(selectCharacterParty(store.getState())[0]?.portrait, undefined,
    "unavailable refresh does not replace a prior denial");

  const extras = Array.from({ length: 9 }, (_, index) => {
    const value = member(); value.id = `actor.extra-${index}`; return value;
  });
  // A minimal same-scope bootstrap omits media; it must retain the denial
  // floor instead of reintroducing the original roster's old portrait.
  store.dispatch(hubActions.bootstrapCommitted({ scope, party: [member(), ...extras] }));
  for (const [index, extra] of extras.entries()) {
    const token = index + 5;
    store.dispatch(hubActions.characterRequestStarted({ scope, actorId: extra.id, facet: "sheet", requestToken: token }));
    store.dispatch(commitCharacterFacet({ scope, actorId: extra.id, facet: "sheet", value: extra },
      { generation: 1, requestToken: token, confirmedAt: token, bytes: 100 }));
  }
  assert.equal(selectCharacterParty(store.getState())[0]?.portrait, undefined,
    "evicting a denied portrait keeps a zero-byte authority floor instead of reviving base media");
});

test("portrait authority is ordered across facets and bootstrap or cache clears cannot revive base media", () => {
  const store = createHubStore();
  const current = envelope();
  const scope = characterScope(current);
  const actor = member({ imageUrl: "/portrait/base", alt: "base", width: 1, height: 1 });
  store.dispatch(hubActions.bootstrapCommitted({ scope, party: [actor] }));
  const denied = member({ imageUrl: "/portrait/denied-copied", alt: "denied", width: 1, height: 1 });
  denied.portraitCoverage = "denied";
  const lateSheet = member({ imageUrl: "/portrait/late", alt: "late", width: 1, height: 1 });
  lateSheet.portraitCoverage = "confirmed";
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "sheet", requestToken: 10 }));
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "details", requestToken: 11 }));
  store.dispatch(commitCharacterFacet({ scope, actorId: actor.id, facet: "details", value: denied },
    { generation: 1, requestToken: 11, confirmedAt: 11, bytes: 100 }));
  store.dispatch(commitCharacterFacet({ scope, actorId: actor.id, facet: "sheet", value: lateSheet },
    { generation: 1, requestToken: 10, confirmedAt: 10, bytes: 100 }));
  assert.equal(selectCharacterParty(store.getState())[0]?.portrait, undefined,
    "a late sheet completion cannot overturn a newer details denial");
  assert.equal(store.getState().confirmed.facetsById[actor.id]?.details?.value.portrait, undefined,
    "denied media is removed from the retained value, not only hidden by its selector");
  const cached = peekCharacterFacet<PartyMemberReadModel>(store.getState(), scope, actor.id, "sheet", Number.MAX_SAFE_INTEGER)!;
  assert.equal(cached.portrait, undefined, "an older cached sheet respects the newer media denial");
  assert.equal(cached.portraitCoverage, "denied");
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "sheet", requestToken: 12 }));
  store.dispatch(commitCharacterFacet({ scope, actorId: actor.id, facet: "sheet", value: cached },
    { generation: 1, requestToken: 12, confirmedAt: 12, bytes: 100 }));
  assert.equal(selectCharacterParty(store.getState())[0]?.portrait, undefined,
    "reusing a cached sheet cannot promote an older portrait over a newer denial");

  const pending = member({ imageUrl: "/portrait/pending", alt: "pending", width: 1, height: 1 });
  pending.portraitCoverage = "confirmed";
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "sheet", requestToken: 12 }));
  const explicitClear = member({ imageUrl: "/portrait/bootstrap-denied", alt: "stale", width: 1, height: 1 });
  explicitClear.portraitCoverage = "denied";
  store.dispatch(hubActions.bootstrapCommitted({ scope, party: [explicitClear] }));
  store.dispatch(commitCharacterFacet({ scope, actorId: actor.id, facet: "sheet", value: pending },
    { generation: 1, requestToken: 12, confirmedAt: 12, bytes: 100 }));
  assert.equal(selectCharacterParty(store.getState())[0]?.portrait, undefined,
    "an explicit same-scope bootstrap clear fences pending pre-bootstrap media reads");

  store.dispatch(hubActions.characterFacetsCleared(undefined));
  assert.equal(selectCharacterParty(store.getState())[0]?.portrait, undefined,
    "clearing evictable facets retains a zero-byte portrait floor instead of the stale roster image");

  const deniedAtFirstBootstrap = member({ imageUrl: "/portrait/first-bootstrap-denied", alt: "stale", width: 1, height: 1 });
  deniedAtFirstBootstrap.portraitCoverage = "denied";
  store.dispatch(hubActions.bootstrapCommitted({ scope: characterScope(envelope("player")), party: [deniedAtFirstBootstrap] }));
  assert.equal(selectCharacterParty(store.getState())[0]?.portrait, undefined,
    "a changed-scope bootstrap sanitizes an inconsistent denied image before base roster display");
});

test("a transient canonical refresh retains the prior same-scope confirmed sheet", () => {
  const store = createHubStore();
  const current = envelope();
  const scope = characterScope(current);
  const actor = member();
  const entry = { id: "sheet.confirmed", kind: "class", title: "Last confirmed sheet", detail: "still visible" };
  const confirmed = member(); Object.assign(confirmed, {
    sheet: [entry], sheetStatus: "canonical", sheetState: { status: "ready", source: "canonical", data: [entry] },
  });
  const failed = member(); Object.assign(failed, {
    sheet: [], sheetStatus: "unavailable", sheetState: {
      status: "error", data: null, failureCategory: "stale-data", diagnosticId: "revision-changed", httpStatus: 409,
    },
  });
  store.dispatch(hubActions.bootstrapCommitted({ scope, party: [actor] }));
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "sheet", requestToken: 1 }));
  store.dispatch(commitCharacterFacet({ scope, actorId: actor.id, facet: "sheet", value: confirmed },
    { generation: 1, requestToken: 1, confirmedAt: 1, bytes: 100 }));
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "sheet", requestToken: 2 }));
  store.dispatch(commitCharacterFacet({ scope, actorId: actor.id, facet: "sheet", value: failed },
    { generation: 1, requestToken: 2, confirmedAt: 2, bytes: 100 }));
  const selected = selectCharacterParty(store.getState())[0]!;
  assert.equal(selected.sheetState.status, "stale");
  assert.equal(selected.sheet[0]?.title, "Last confirmed sheet");
  assert.equal(peekCharacterFacet(store.getState(), scope, actor.id, "sheet", Number.MAX_SAFE_INTEGER), null,
    "stale display data must not satisfy a recovery read as fresh");
});

test("failed character and inventory results stay visible but cannot satisfy fresh-cache reads", () => {
  const store = createHubStore();
  const scope = characterScope(envelope());
  const actor = member();
  store.dispatch(hubActions.bootstrapCommitted({ scope, party: [actor] }));
  let requestToken = 0;
  for (const facet of ["sheet", "details"] as const) {
    const failed = member();
    failed.sheetState = { status: "error", data: null, failureCategory: "transport", diagnosticId: "offline" };
    store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet, requestToken: ++requestToken }));
    store.dispatch(commitCharacterFacet({ scope, actorId: actor.id, facet, value: failed },
      { generation: 1, requestToken, confirmedAt: Date.now(), bytes: 100 }));
    assert.equal(peekCharacterFacet(store.getState(), scope, actor.id, facet, 60_000), null);
    assert.equal(store.getState().confirmed.facetsById[actor.id]?.[facet]?.value.sheetState.status, "error");
  }
  const failedInventory: InventoryContainerResult = {
    status: "error", data: null, failureCategory: "transport", diagnosticId: "offline-inventory",
  };
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "inventory", requestToken: ++requestToken }));
  store.dispatch(commitInventory({ scope, actorId: actor.id, value: failedInventory },
    { generation: 1, requestToken, confirmedAt: Date.now(), bytes: 100 }));
  assert.equal(peekCharacterFacet(store.getState(), scope, actor.id, "inventory", 60_000), null);
  assert.equal(selectCharacterMember(actor.id)(store.getState())?.inventoryResource, failedInventory);
});

test("available character fields remain cacheable independently of optional dossier fields, but failed media is reread", () => {
  const store = createHubStore();
  const scope = characterScope(envelope());
  const actor = member();
  store.dispatch(hubActions.bootstrapCommitted({ scope, party: [actor] }));
  const partial = member();
  partial.inventoryState = { status: "error", data: null, failureCategory: "incompatible-data", diagnosticId: "optional-inventory" };
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "details", requestToken: 1 }));
  store.dispatch(commitCharacterFacet({ scope, actorId: actor.id, facet: "details", value: partial },
    { generation: 1, requestToken: 1, confirmedAt: Date.now(), bytes: 100 }));
  assert.equal(peekCharacterFacet(store.getState(), scope, actor.id, "details", 60_000), partial);
  const unavailableMedia = { ...partial, portraitCoverage: "unavailable" as const };
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "details", requestToken: 2 }));
  store.dispatch(commitCharacterFacet({ scope, actorId: actor.id, facet: "details", value: unavailableMedia },
    { generation: 1, requestToken: 2, confirmedAt: Date.now(), bytes: 100 }));
  assert.equal(peekCharacterFacet(store.getState(), scope, actor.id, "details", 60_000), null);
  assert.equal(selectCharacterMember(actor.id)(store.getState())?.sheetState.status, "ready");
});

test("ItemWorkspace header stays subscribed to the confirmed portrait through a minimal item-route bootstrap", async () => {
  const store = createHubStore();
  const current = envelope();
  const scope = characterScope(current);
  const portrait = { imageUrl: "/portrait/route", alt: "Ganji portrait", width: 100, height: 150 };
  store.dispatch(hubActions.bootstrapCommitted({ scope, party: [member(portrait)] }));
  const dom = new JSDOM("<!doctype html><div id='root'></div>", { url: "https://table.test/#item" });
  const previous = ["window", "document", "HTMLElement", "Element", "Node", "Event", "IS_REACT_ACT_ENVIRONMENT"]
    .map((key) => [key, Object.getOwnPropertyDescriptor(globalThis, key)] as const);
  for (const [key] of previous) Object.defineProperty(globalThis, key, {
    configurable: true, writable: true, value: key === "IS_REACT_ACT_ENVIRONMENT" ? true : dom.window[key as keyof Window],
  });
  try {
    const { createRoot } = await import("react-dom/client");
    const root = createRoot(dom.window.document.getElementById("root")!);
    function ItemRoute() {
      const party = useHubSelector(selectCharacterParty);
      return <ItemWorkspace route={{ kind: "item", campaignId: "campaign.fixture", perspective: "dm",
        characterId: "actor.fixture", itemId: "item.fixture", tab: "details" }}
        campaignId="campaign.fixture" perspective="dm" context={current} party={party} />;
    }
    await act(async () => { root.render(<Provider store={store}><ItemRoute /></Provider>); });
    assert.equal(dom.window.document.querySelector(".character-roster__portrait img")?.getAttribute("src"), "/portrait/route");
    await act(async () => {
      store.dispatch(hubActions.bootstrapCommitted({ scope, party: [member()] }));
    });
    assert.equal(dom.window.document.querySelector(".character-roster__portrait img")?.getAttribute("src"), "/portrait/route",
      "the item header uses the same subscribed confirmed member rather than a minimal route response");
    await act(async () => root.unmount());
  } finally {
    for (const [key, descriptor] of previous) {
      if (descriptor) Object.defineProperty(globalThis, key, descriptor); else Reflect.deleteProperty(globalThis, key);
    }
    dom.window.close();
  }
});

test("CharacterResourceOwner reads a same-scope Redux facet and never retains container pages", async () => {
  const store = createHubStore();
  const current = envelope();
  const currentScope = characterScope(current);
  store.dispatch(hubActions.bootstrapCommitted({ scope: currentScope, party: [member()] }));
  let sheetReads = 0;
  let pageReads = 0;
  const sheet = member({ imageUrl: "/portrait/fresh", alt: "Ganji", width: 100, height: 150 });
  const owner = new CharacterResourceOwner({
    readSheet: async () => { sheetReads += 1; return sheet; },
    readDetails: async () => member(),
    readInventory: async () => ({ status: "ready" } as InventoryContainerResult),
    readInventoryContainer: async () => { pageReads += 1; return { status: "ready" } as InventoryContainerPageResult; },
    readConfirmed: (facet, request, maximumAgeMs) => peekCharacterFacet(
      store.getState(), characterScope(request.envelope), request.actorId, facet, maximumAgeMs),
    clearConfirmed: () => store.dispatch(hubActions.characterFacetsCleared(undefined)),
    clearScope: () => store.dispatch(hubActions.scopeCleared(undefined)),
  });
  const request = { envelope: current, actorId: "actor.fixture" };
  assert.equal(await owner.loadSheet(request), sheet);
  store.dispatch(hubActions.characterRequestStarted({ scope: currentScope, actorId: request.actorId, facet: "sheet", requestToken: 1 }));
  store.dispatch(commitCharacterFacet({ scope: currentScope, actorId: request.actorId,
    facet: "sheet", value: sheet }, { confirmedAt: Date.now(), generation: 1, requestToken: 1, bytes: 100 }));
  assert.equal(await owner.loadSheet(request), sheet);
  assert.equal(sheetReads, 1, "preferCached reads the canonical store rather than a resource response cache");

  const page = { ...request, containerId: "item.bag" };
  await owner.loadInventoryContainer(page);
  await owner.loadInventoryContainer(page);
  assert.equal(pageReads, 2, "container-page results remain query-scoped until collection coverage is proven");
  assert.equal(owner.cacheMetrics().retainedEntries, 0);
});

test("CharacterResourceOwner marks Redux hits explicitly without mistaking a same-reference fresh result for one", async () => {
  const store = createHubStore();
  const current = envelope();
  const scope = characterScope(current);
  const actor = member();
  const sheet = member({ imageUrl: "/portrait/sheet", alt: "Sheet", width: 1, height: 1 });
  const details = member({ imageUrl: "/portrait/details", alt: "Details", width: 1, height: 1 });
  const inventory = { status: "ready", data: { items: [] } } as InventoryContainerResult;
  const reads = { sheet: 0, details: 0, inventory: 0 };
  store.dispatch(hubActions.bootstrapCommitted({ scope, party: [actor] }));
  const owner = new CharacterResourceOwner({
    readSheet: async () => { reads.sheet += 1; return sheet; },
    readDetails: async () => { reads.details += 1; return details; },
    readInventory: async () => { reads.inventory += 1; return inventory; },
    readConfirmed: (facet, request, age) => peekCharacterFacet(
      store.getState(), characterScope(request.envelope), request.actorId, facet, age),
    clearConfirmed: () => store.dispatch(hubActions.characterFacetsCleared(undefined)),
    clearScope: () => store.dispatch(hubActions.scopeCleared(undefined)),
  });
  const request = { envelope: current, actorId: actor.id };
  const firstSheet = await owner.loadSheetOutcome(request, undefined, false);
  const firstDetails = await owner.loadDetailsOutcome(request, undefined, false);
  const firstInventory = await owner.loadInventoryOutcome(request, undefined, false);
  assert.equal(firstSheet.cacheHit, false);
  assert.equal(firstDetails.cacheHit, false);
  assert.equal(firstInventory.cacheHit, false);
  assert.equal(firstSheet.value, sheet);
  assert.equal(firstDetails.value, details);
  assert.equal(firstInventory.value, inventory);

  const confirmedAt = { sheet: Date.now() - 100, details: Date.now() - 90, inventory: Date.now() - 80 };
  for (const [facet, value, requestToken] of [
    ["sheet", sheet, 1], ["details", details, 2],
  ] as const) {
    store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet, requestToken }));
    store.dispatch(commitCharacterFacet({ scope, actorId: actor.id, facet, value },
      { generation: 1, requestToken, confirmedAt: confirmedAt[facet], bytes: 100 }));
  }
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "inventory", requestToken: 3 }));
  store.dispatch(commitInventory({ scope, actorId: actor.id, value: inventory },
    { generation: 1, requestToken: 3, confirmedAt: confirmedAt.inventory, bytes: 100 }));

  const hits = await Promise.all([
    owner.loadSheetOutcome(request), owner.loadDetailsOutcome(request), owner.loadInventoryOutcome(request),
  ]);
  assert.ok(hits.every((result) => result.cacheHit));
  assert.deepEqual(reads, { sheet: 1, details: 1, inventory: 1 });

  for (const [facet, requestToken] of [["sheet", 4], ["details", 5], ["inventory", 6]] as const) {
    store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet, requestToken }));
    store.dispatch(hubActions.characterRequestFinished({ scope, actorId: actor.id, facet, requestToken }));
    assert.equal(store.getState().confirmed.facetsById[actor.id]?.[facet]?.confirmedAt, confirmedAt[facet],
      `a ${facet} cache hit does not renew confirmation time`);
  }
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "sheet", requestToken: 7 }));
  store.dispatch(hubActions.characterRequestFinished({ scope, actorId: actor.id, facet: "sheet", requestToken: 8 }));
  assert.equal(store.getState().confirmed.requestsByKey[`${actor.id}|sheet`]?.requestToken, 7,
    "a cache completion cannot finish a newer request");

  store.dispatch(hubActions.characterFacetsCleared(undefined));
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "sheet", requestToken: 9 }));
  store.dispatch(hubActions.characterRequestFinished({ scope, actorId: actor.id, facet: "sheet", requestToken: 7 }));
  assert.equal(store.getState().confirmed.requestsByKey[`${actor.id}|sheet`]?.requestToken, 9,
    "an invalidated cache completion cannot finish a new generation's request");
  const otherScope = characterScope(envelope("player"));
  store.dispatch(hubActions.bootstrapCommitted({ scope: otherScope, party: [actor] }));
  store.dispatch(hubActions.characterRequestStarted({ scope: otherScope, actorId: actor.id, facet: "sheet", requestToken: 10 }));
  store.dispatch(hubActions.characterRequestFinished({ scope, actorId: actor.id, facet: "sheet", requestToken: 9 }));
  assert.equal(store.getState().confirmed.requestsByKey[`${actor.id}|sheet`]?.requestToken, 10,
    "an old-scope cache completion cannot finish the current scope's request");

  const sameReferenceFresh = await owner.loadSheetOutcome(request, undefined, false);
  assert.equal(sameReferenceFresh.cacheHit, false, "fresh transport provenance never depends on value identity");
  assert.equal(sameReferenceFresh.value, sheet);
  owner.invalidateAll();
  const afterInvalidation = await owner.loadInventoryOutcome(request);
  assert.equal(afterInvalidation.cacheHit, false, "invalidated canonical data cannot be revived as a hit");
  assert.equal(reads.inventory, 2);
});

test("the real character owner rereads a recently failed Redux facet after the host recovers", async () => {
  const store = createHubStore();
  const current = envelope();
  const scope = characterScope(current);
  const actor = member();
  const failed = member();
  failed.sheetState = { status: "error", data: null, failureCategory: "transport", diagnosticId: "offline" };
  const recovered = member({ imageUrl: "/portrait/recovered", alt: "Recovered", width: 1, height: 1 });
  recovered.portraitCoverage = "confirmed";
  store.dispatch(hubActions.bootstrapCommitted({ scope, party: [actor] }));
  let calls = 0;
  const owner = new CharacterResourceOwner({
    readSheet: async () => ++calls === 1 ? failed : recovered,
    readDetails: async () => member(),
    readInventory: async () => ({ status: "ready" } as InventoryContainerResult),
    readInventoryContainer: async () => ({ status: "ready" } as InventoryContainerPageResult),
    readConfirmed: (facet, request, age) => peekCharacterFacet(store.getState(), characterScope(request.envelope), request.actorId, facet, age),
  });
  const request = { envelope: current, actorId: actor.id };
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "sheet", requestToken: 1 }));
  store.dispatch(commitCharacterFacet({ scope, actorId: actor.id, facet: "sheet", value: await owner.loadSheet(request) },
    { generation: 1, requestToken: 1, confirmedAt: Date.now(), bytes: 100 }));
  assert.equal(selectCharacterMember(actor.id)(store.getState())?.sheetState.status, "error");
  assert.equal(await owner.loadSheet(request), recovered);
  assert.equal(calls, 2, "a new authorized request reaches the host rather than returning its recent failure");
});
