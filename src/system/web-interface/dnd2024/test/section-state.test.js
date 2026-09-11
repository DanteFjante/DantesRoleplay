import assert from "node:assert/strict";
import test from "node:test";

import { applyDeferredHubUpdate, preserveLastGoodPartyData } from "../src/data/section-state.ts";
import { normalizeMapId } from "../src/state.js";

function member(state) {
  const data = [{ id: "sheet-1", kind: "class", title: "Bard", detail: "Canonical bard." }];
  return {
    id: "actor.one",
    initials: "AO",
    name: "Actor One",
    detail: "Bard",
    status: "Active",
    isCurrent: true,
    recordStatus: state.status === "ready" ? "Canonical character state" : "Canonical character unavailable",
    sheetStatus: state.status === "ready" ? "canonical" : "unavailable",
    inventoryStatus: state.status === "ready" ? "empty" : "unavailable",
    sheetState: state.status === "ready"
      ? { status: "ready", source: "canonical", data }
      : state,
    inventoryState: state.status === "ready"
      ? { status: "empty", source: "canonical", data: [] }
      : state,
    sheet: state.status === "ready" ? data : [],
    inventory: [],
    knowledge: [],
    backstory: [],
    origin: [],
    ...(state.status === "ready" ? { characterSheet: { version: 1, subject: { id: "actor.one", name: "Actor One" } } } : {}),
  };
}

function envelope(party, perspective = "player", campaignId = "campaign.one", options = {}) {
  return {
    status: "ready",
    version: 1,
    applicationId: options.applicationId ?? "dnd2024",
    stateSpaceId: options.stateSpaceId ?? "campaign.fixture",
    revision: options.revision ?? `live:dnd2024:campaign.fixture:${campaignId}`,
    audience: {
      seat: options.seat ?? "player",
      perspective,
      allowedPerspectives: options.allowedPerspectives ?? ["player"],
    },
    contextSelection: { selectedCampaignId: campaignId },
    party,
  };
}

test("deferred feature updates preserve independently loaded hub data", () => {
  const current = {
    ...envelope([]),
    contextSelection: { selectedCampaignId: "campaign.one", selectedWorldId: "world.one", worlds: [] },
    currentSituation: { status: "unavailable", message: "Not loaded" },
    world: {
      history: [{ id: "history.old" }],
      lore: [{ id: "lore.keep" }],
      locations: [{ id: "location.keep" }],
      people: [{ id: "person.keep" }],
      factions: [{ id: "faction.keep" }],
      factionDirectory: { totalCount: 1, complete: true, nextCursor: null },
    },
    campaign: { quests: [{ id: "quest.keep" }], clues: [], mapOverlays: [] },
  };
  const updated = applyDeferredHubUpdate(current, {
    section: "history",
    world: { history: [{ id: "history.new" }] },
  });
  assert.deepEqual(updated.world.history, [{ id: "history.new" }]);
  assert.equal(updated.world.lore, current.world.lore);
  assert.equal(updated.world.factions, current.world.factions);
  assert.equal(updated.world.factionDirectory, current.world.factionDirectory);
  assert.equal(updated.campaign, current.campaign);
  assert.equal(updated.currentSituation, current.currentSituation);
});

test("location scope pages merge by identity without replacing other loaded branches", () => {
  const place = (id, name, people = [], routes = []) => ({ id, name, region: "Test", kind: "site",
    status: "Known", summary: name, description: name, atmosphere: "", landmarks: [], observations: [],
    routes, mapAnchor: { x: 0, y: 0 }, people });
  const scope = (id, parentId, childIds, nextCursor = null) => ({ id, name: id, parentId, childIds,
    totalCount: childIds.length + (nextCursor ? 1 : 0), complete: nextCursor === null, nextCursor,
    sourceRevisionFingerprint: "A".repeat(64) });
  const current = {
    ...envelope([]),
    contextSelection: { selectedCampaignId: "campaign.one", selectedWorldId: "world.one", worlds: [] },
    world: {
      id: "world.one", currentLocationId: "a.old", map: { imageUrl: "", alt: "Map" },
      mapOwnerId: null, rootMapId: "map.root", regions: [], facts: [], history: [], lore: [], people: [], factions: [],
      maps: [
        { id: "map.root", subject: { id: "world.one" } },
        { id: "map.a", subject: { id: "region.a" } },
        { id: "map.b", subject: { id: "region.b" } },
      ],
      locations: [place("region.a", "A"), place("region.b", "B"),
        place("a.old", "Old", [{ id: "person.keep" }], [{ destination: "Keep", detail: "Known" }]),
        place("b.keep", "Other branch")],
      locationScopes: [scope("world.one", null, ["region.a", "region.b"]),
        scope("region.a", "world.one", ["a.old"], "100"), scope("region.b", "world.one", ["b.keep"])],
    },
    campaign: { mapOverlays: [{ id: "overlay.a", mapId: "map.a" }, { id: "overlay.b", mapId: "map.b" }] },
  };
  const updated = applyDeferredHubUpdate(current, {
    section: "locations", scopePage: { id: "region.a" },
    world: {
      currentLocationId: "a.old", map: { imageUrl: "/wrong-nested-root.png", alt: "Wrong" },
      mapOwnerId: "region.a", rootMapId: "map.wrong-nested-root",
      maps: [{ id: "map.root", subject: { id: "world.one" } }, { id: "map.a", subject: { id: "region.a" } }],
      regions: [], facts: [],
      locations: [place("region.a", "A refreshed"), place("a.old", "Old refreshed"), place("a.new", "New")],
      locationScopes: [scope("region.a", "world.one", ["a.old", "a.new"])],
    },
    campaign: { mapOverlays: [] },
  });

  assert.deepEqual(updated.world.locationScopes.find((item) => item.id === "region.a").childIds,
    ["a.old", "a.new"]);
  assert.equal(updated.world.locations.find((item) => item.id === "a.old").name, "Old refreshed");
  assert.deepEqual(updated.world.locations.find((item) => item.id === "a.old").people, [{ id: "person.keep" }]);
  assert.equal(updated.world.locations.some((item) => item.id === "b.keep"), true,
    "a page does not replace a separately loaded branch");
  assert.equal(updated.world.maps.some((item) => item.id === "map.b"), true);
  assert.equal(updated.world.rootMapId, "map.root", "a nested page cannot replace the root map selection");
  assert.equal(updated.world.map.imageUrl, "", "a nested page cannot replace the root map fallback");
  assert.deepEqual(updated.campaign.mapOverlays.map((item) => item.id), ["overlay.a", "overlay.b"],
    "a location page cannot erase independently loaded Lore overlays");
});

test("a root scope page replaces the synthetic world map with the discovered atlas", () => {
  const current = {
    ...envelope([]),
    contextSelection: { selectedCampaignId: "campaign.one", selectedWorldId: "world.caldris", worlds: [] },
    world: {
      id: "world.caldris", currentLocationId: "location.caldris.atlas", map: { imageUrl: "", alt: "Placeholder" },
      mapOwnerId: null, rootMapId: "map.synthetic-world", maps: [
        { id: "map.synthetic-world", parentMapId: null, subject: { id: "world.caldris" } },
      ],
      locations: [{ id: "location.caldris.atlas" }],
      locationScopes: [{ id: "world.caldris", name: "Caldris", parentId: null,
        childIds: ["location.caldris.atlas"], totalCount: 1, complete: true, nextCursor: null }],
    },
    campaign: { mapOverlays: [] },
  };
  const updated = applyDeferredHubUpdate(current, {
    section: "locations", scopePage: { id: "world.caldris" },
    world: {
      currentLocationId: "location.caldris.atlas", map: { imageUrl: "/atlas.png", alt: "Veyr Marches" },
      mapOwnerId: "location.caldris.atlas", rootMapId: "map.caldris-atlas",
      maps: [{ id: "map.caldris-atlas", parentMapId: null, subject: { id: "location.caldris.atlas" } }],
      regions: [], facts: [], locations: [{ id: "location.caldris.atlas" }],
      locationScopes: [{ id: "world.caldris", name: "Caldris", parentId: null,
        childIds: ["location.caldris.atlas"], totalCount: 1, complete: true, nextCursor: null }],
    },
    campaign: { mapOverlays: [] },
  });

  assert.deepEqual(updated.world.maps.map((map) => map.id), ["map.caldris-atlas"]);
  assert.equal(updated.world.maps.some((map) => map.id === updated.world.rootMapId), true,
    "the declared real root map remains included");
  assert.equal(normalizeMapId(updated.world.maps, "map.synthetic-world", updated.world.rootMapId),
    "map.caldris-atlas", "an old selected placeholder normalizes to the discovered root");
  assert.equal(updated.world.map.imageUrl, "/atlas.png");
});

test("a root refresh does not delete a previously real map when its owner was known", () => {
  const current = {
    ...envelope([]),
    contextSelection: { selectedCampaignId: "campaign.one", selectedWorldId: "world.caldris", worlds: [] },
    world: {
      id: "world.caldris", currentLocationId: "location.caldris.atlas", map: { imageUrl: "/old.png", alt: "Old" },
      mapOwnerId: "location.caldris.old", rootMapId: "map.caldris-old", maps: [
        { id: "map.caldris-old", subject: { id: "location.caldris.old" } },
      ],
      locations: [{ id: "location.caldris.old" }, { id: "location.caldris.atlas" }],
      locationScopes: [{ id: "world.caldris", name: "Caldris", parentId: null,
        childIds: ["location.caldris.old", "location.caldris.atlas"], totalCount: 2, complete: true, nextCursor: null }],
    },
    campaign: { mapOverlays: [] },
  };
  const updated = applyDeferredHubUpdate(current, {
    section: "locations", scopePage: { id: "world.caldris" },
    world: {
      currentLocationId: "location.caldris.atlas", map: { imageUrl: "/atlas.png", alt: "Veyr Marches" },
      mapOwnerId: "location.caldris.atlas", rootMapId: "map.caldris-atlas",
      maps: [{ id: "map.caldris-atlas", subject: { id: "location.caldris.atlas" } }],
      regions: [], facts: [], locations: [{ id: "location.caldris.atlas" }],
      locationScopes: [{ id: "world.caldris", name: "Caldris", parentId: null,
        childIds: ["location.caldris.old", "location.caldris.atlas"], totalCount: 2, complete: true, nextCursor: null }],
    },
    campaign: { mapOverlays: [] },
  });

  assert.equal(updated.world.maps.some((map) => map.id === "map.caldris-old"), true,
    "a known-owner map is retained as a reachable real record");
  assert.equal(updated.world.maps.some((map) => map.id === "map.caldris-atlas"), true);
});

test("refresh failure preserves canonical data as stale and a later success restores ready", () => {
  const ready = envelope([member({ status: "ready" })]);
  const failedState = {
    status: "error",
    data: null,
    failureCategory: "transport",
    diagnosticId: "request-500",
  };
  const failed = envelope([member(failedState)]);

  const stale = preserveLastGoodPartyData(ready, failed);
  assert.equal(stale.party[0].sheetState.status, "stale");
  assert.equal(stale.party[0].sheetState.diagnosticId, "request-500");
  assert.equal(stale.party[0].sheet[0].title, "Bard");
  assert.equal(stale.party[0].recordStatus, "Canonical character state is stale");

  const recovered = preserveLastGoodPartyData(stale, ready);
  assert.equal(recovered.party[0].sheetState.status, "ready");
  assert.equal(recovered.party[0].recordStatus, "Canonical character state");
});

test("first failure stays error because no last-good canonical data exists", () => {
  const failedState = {
    status: "error",
    data: null,
    failureCategory: "incompatible-data",
    diagnosticId: "malformed-1",
  };
  const failed = envelope([member(failedState)]);
  const result = preserveLastGoodPartyData(envelope([]), failed);
  assert.equal(result.party[0].sheetState.status, "error");
  assert.deepEqual(result.party[0].sheet, []);
});

test("authorization loss never preserves previously readable character data", () => {
  const ready = envelope([member({ status: "ready" })]);
  const forbiddenState = {
    status: "forbidden",
    data: null,
    failureCategory: "authorization",
    diagnosticId: "authorization-revoked",
  };
  const forbidden = envelope([member(forbiddenState)]);

  const result = preserveLastGoodPartyData(ready, forbidden);
  assert.equal(result.party[0].sheetState.status, "forbidden");
  assert.deepEqual(result.party[0].sheet, []);
  assert.equal(result.party[0].characterSheet, undefined);
});

test("non-transient catalog and incompatible-data failures never reuse last-good values", () => {
  const ready = envelope([member({ status: "ready" })]);
  for (const failedState of [
    {
      status: "error",
      data: null,
      failureCategory: "http",
      diagnosticId: "catalog-422",
      httpStatus: 422,
    },
    {
      status: "error",
      data: null,
      failureCategory: "incompatible-data",
      diagnosticId: "malformed-projection",
    },
  ]) {
    const result = preserveLastGoodPartyData(ready, envelope([member(failedState)]));
    assert.equal(result.party[0].sheetState.status, "error");
    assert.deepEqual(result.party[0].sheet, []);
    assert.equal(result.party[0].characterSheet, undefined);
  }
});

test("transient HTTP failures may reuse canonical data and retain the response status", () => {
  const ready = envelope([member({ status: "ready" })]);
  const failedState = {
    status: "error",
    data: null,
    failureCategory: "http",
    diagnosticId: "temporary-503",
    httpStatus: 503,
  };
  const result = preserveLastGoodPartyData(ready, envelope([member(failedState)]));
  assert.equal(result.party[0].sheetState.status, "stale");
  assert.equal(result.party[0].sheetState.httpStatus, 503);
  assert.equal(result.party[0].sheet[0].title, "Bard");
});

test("stale fingerprints retain last-good canonical data and their stable error code", () => {
  const ready = envelope([member({ status: "ready" })]);
  const failedState = {
    status: "error",
    data: null,
    failureCategory: "stale-data",
    diagnosticId: "stale-request",
    errorCode: "READ_MODEL_STATE_SPACE_STALE",
    httpStatus: 409,
  };
  const result = preserveLastGoodPartyData(ready, envelope([member(failedState)]));
  assert.equal(result.party[0].sheetState.status, "stale");
  assert.equal(result.party[0].sheetState.errorCode, "READ_MODEL_STATE_SPACE_STALE");
  assert.equal(result.party[0].sheet[0].title, "Bard");
});

test("last-good data is isolated by campaign, perspective, audience, and source revision", () => {
  const ready = envelope([member({ status: "ready" })], "player", "campaign.one");
  const failure = member({
    status: "error",
    data: null,
    failureCategory: "transport",
    diagnosticId: "temporary-network-failure",
  });
  const changedBoundaries = [
    envelope([failure], "player", "campaign.two"),
    envelope([failure], "dm", "campaign.one", { seat: "dm", allowedPerspectives: ["dm", "player"] }),
    envelope([failure], "player", "campaign.one", { seat: "dm", allowedPerspectives: ["dm", "player"] }),
    envelope([failure], "player", "campaign.one", { revision: "live:dnd2024:campaign.fixture:revision-two" }),
    envelope([failure], "player", "campaign.one", { stateSpaceId: "campaign.other" }),
  ];

  for (const next of changedBoundaries) {
    const result = preserveLastGoodPartyData(ready, next);
    assert.equal(result.party[0].sheetState.status, "error");
    assert.deepEqual(result.party[0].sheet, []);
  }
});
