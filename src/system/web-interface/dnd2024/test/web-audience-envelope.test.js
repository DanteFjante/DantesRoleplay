import assert from "node:assert/strict";
import test from "node:test";

import {
  parseDmEmails,
  parseDmPrincipalIds,
  resolveAudience,
} from "../src/server/audience-policy.js";
import { projectHubEnvelope, readHubEnvelope } from "../src/server/hub-envelope.js";
import {
  CAMPAIGN_SECRET_CANARIES,
  FACTION_SECRET_CANARIES,
  HIDDEN_CAMPAIGN_CANARIES,
  HIDDEN_HISTORY_CANARIES,
  HIDDEN_FACTION_CANARIES,
  DM_ONLY_BASE_CANARIES,
  DM_ONLY_LAYER_CANARIES,
  HIDDEN_LORE_CANARIES,
  HIDDEN_MAP_CANARIES,
  HIDDEN_MAP_FEATURE_CANARIES,
  HIDDEN_OVERLAY_CANARIES,
  UNREACHABLE_OVERLAY_CANARIES,
  HIDDEN_PERSON_CANARIES,
  HIDDEN_LOCATION_CANARIES,
  HOLDING_CANARIES,
  HISTORY_SECRET_CANARIES,
  LORE_SECRET_CANARIES,
  HUB_SOURCE_REVISION,
  PERSON_SECRET_CANARIES,
  SECRET_CANARIES,
  hubSource,
} from "../src/server/hub-source.js";

const dmPrincipal = "principal.dm.fixture";

function audience(principal, requested) {
  return resolveAudience({
    authenticatedUserId: principal,
    authenticatedUserEmail: "",
    requestedPerspective: requested,
    dmPrincipalIds: [dmPrincipal],
  });
}

test("only an exact server allowlist entry receives the DM seat", () => {
  assert.deepEqual(parseDmPrincipalIds(`${dmPrincipal},principal.other`), [dmPrincipal, "principal.other"]);
  assert.deepEqual(parseDmPrincipalIds(`${dmPrincipal},${dmPrincipal}`), []);
  assert.deepEqual(parseDmPrincipalIds(` ${dmPrincipal}`), []);

  assert.equal(audience(dmPrincipal, "dm").seat, "dm");
  assert.equal(audience("principal.player.fixture", "dm").seat, "player");
  assert.equal(audience("principal.player.fixture", "dm").perspective, "player");
});

test("an exact trusted email can issue DM without exposing or weakening the seat", () => {
  const configuredEmail = "keeper@example.test";
  assert.deepEqual(parseDmEmails("Keeper@example.test,other@example.test"), [
    configuredEmail,
    "other@example.test",
  ]);
  assert.deepEqual(parseDmEmails(` ${configuredEmail}`), []);
  assert.deepEqual(parseDmEmails(`${configuredEmail},KEEPER@example.test`), []);
  assert.deepEqual(parseDmEmails("not-an-email"), []);

  const dm = resolveAudience({
    authenticatedUserId: "site-scoped-owner-id",
    authenticatedUserEmail: "KEEPER@example.test",
    requestedPerspective: "dm",
    dmPrincipalIds: [],
    dmEmails: [configuredEmail],
  });
  const player = resolveAudience({
    authenticatedUserId: "site-scoped-player-id",
    authenticatedUserEmail: "player@example.test",
    requestedPerspective: "dm",
    dmPrincipalIds: [],
    dmEmails: [configuredEmail],
  });

  assert.equal(dm.seat, "dm");
  assert.deepEqual(dm.allowedPerspectives, ["dm", "player"]);
  assert.equal(player.seat, "player");
  assert.equal(player.perspective, "player");
});

test("missing identity denies in production while an explicit host development seat is allowed", () => {
  assert.deepEqual(audience("", "player"), { status: "denied" });
  assert.equal(
    resolveAudience({
      authenticatedUserId: "",
      authenticatedUserEmail: "",
      requestedPerspective: "dm",
      dmPrincipalIds: [],
      nodeEnvironment: "development",
      localSeat: "dm",
    }).perspective,
    "dm",
  );
});

test("Player serialization excludes every secret marker and canary", () => {
  const response = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "dm"),
  );
  const serialized = JSON.stringify(response);

  assert.equal(response.audience.perspective, "player");
  assert.equal(serialized.includes("dmSecret"), false);
  assert.equal(serialized.includes('"dm":'), false);
  for (const secret of SECRET_CANARIES) assert.equal(serialized.includes(secret), false);
  for (const hiddenLocation of HIDDEN_LOCATION_CANARIES) {
    assert.equal(serialized.includes(hiddenLocation), false);
  }
  for (const privatePersonFact of PERSON_SECRET_CANARIES) {
    assert.equal(serialized.includes(privatePersonFact), false);
  }
  for (const hiddenPerson of HIDDEN_PERSON_CANARIES) {
    assert.equal(serialized.includes(hiddenPerson), false);
  }
  for (const holdingFact of HOLDING_CANARIES) {
    assert.equal(serialized.includes(holdingFact), false);
  }
  for (const historySecret of HISTORY_SECRET_CANARIES) {
    assert.equal(serialized.includes(historySecret), false);
  }
  for (const hiddenEvent of HIDDEN_HISTORY_CANARIES) {
    assert.equal(serialized.includes(hiddenEvent), false);
  }
  for (const factionSecret of FACTION_SECRET_CANARIES) {
    assert.equal(serialized.includes(factionSecret), false);
  }
  for (const hiddenFaction of HIDDEN_FACTION_CANARIES) {
    assert.equal(serialized.includes(hiddenFaction), false);
  }
  for (const loreSecret of LORE_SECRET_CANARIES) {
    assert.equal(serialized.includes(loreSecret), false);
  }
  for (const hiddenLore of HIDDEN_LORE_CANARIES) {
    assert.equal(serialized.includes(hiddenLore), false);
  }
  for (const campaignSecret of CAMPAIGN_SECRET_CANARIES) {
    assert.equal(serialized.includes(campaignSecret), false);
  }
  for (const hiddenCampaignFact of HIDDEN_CAMPAIGN_CANARIES) {
    assert.equal(serialized.includes(hiddenCampaignFact), false);
  }
  assert.equal(serialized.includes('"holdings":'), false);
  assert.equal(serialized.includes('"motive":'), false);
  assert.equal(serialized.includes('"dmTruth":'), false);
  assert.equal(serialized.includes('"dmConsequence":'), false);
  assert.equal(serialized.includes('"dmAgenda":'), false);
  assert.equal(serialized.includes('"dmNote":'), false);
  assert.equal(serialized.includes('"dmThread":'), false);
  assert.equal(serialized.includes('"dmContext":'), false);
  assert.equal(serialized.includes('"dmRamification":'), false);
  assert.equal(serialized.includes('"dmReveal":'), false);
  assert.equal(serialized.includes('"dmConnection":'), false);
  assert.equal(serialized.includes('"playerKnown":'), false);
});

test("Campaign continuity is projected by audience and links only to visible World records", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );
  const playerLatest = player.campaign.adventureLog.find(
    (entry) => entry.id === "campaign-log.fixture.eastern-ward",
  );

  assert.equal(dm.campaign.adventureLog.length, 6);
  assert.equal(player.campaign.adventureLog.length, 5);
  assert.equal(dm.campaign.placesVisited.length, 5);
  assert.equal(player.campaign.placesVisited.length, 5);
  assert.equal(dm.campaign.outcomes.length, 5);
  assert.equal(player.campaign.outcomes.length, 4);
  assert.equal(dm.campaign.quests.length, 4);
  assert.equal(player.campaign.quests.length, 3);
  assert.equal(dm.campaign.threads.length, 4);
  assert.equal(player.campaign.threads.length, 3);
  assert.equal(dm.campaign.clues.length, 5);
  assert.equal(player.campaign.clues.length, 4);
  assert.equal(typeof dm.campaign.dmContext, "string");
  assert.equal(Object.hasOwn(player.campaign, "dmContext"), false);
  assert.equal(typeof dm.campaign.adventureLog[0].dmNote, "string");
  assert.equal(Object.hasOwn(playerLatest, "dmNote"), false);
  assert.deepEqual(playerLatest.links.locations, [{ id: "sunken-archive", name: "The Sunken Archive" }]);
  assert.deepEqual(playerLatest.links.people.map(({ id }) => id), ["maelin-quill", "brother-caldus"]);
  assert.deepEqual(playerLatest.links.factions, [
    { id: "faction.fixture.lantern-concord", name: "The Lantern Concord" },
  ]);
  assert.equal(player.campaign.adventureLog.some((entry) => entry.id === "campaign-log.fixture.courier-exchange"), false);
  assert.equal(player.campaign.outcomes.some((outcome) => outcome.id === "campaign-outcome.fixture.drowned-invitation"), false);
  assert.equal(player.campaign.quests.some((quest) => quest.id === "campaign-quest.fixture.drowned-court"), false);
  assert.equal(player.campaign.quests[0].objectives.some((objective) => objective.id === "quest-objective.fixture.curator"), false);
  assert.equal(player.campaign.threads.some((thread) => thread.id === "campaign-thread.fixture.white-fox-courier"), false);
  assert.equal(player.campaign.clues.some((clue) => clue.id === "campaign-clue.fixture.drowned-invitation"), false);
  assert.equal(Object.hasOwn(player.campaign.quests[0], "dmContext"), false);
  assert.equal(typeof dm.campaign.quests[0].dmContext, "string");
  assert.equal(Object.hasOwn(player.campaign.threads[0], "dmTruth"), false);
  assert.equal(typeof dm.campaign.threads[0].dmTruth, "string");
  assert.equal(Object.hasOwn(player.campaign.clues[0], "dmConnection"), false);
  assert.equal(typeof dm.campaign.clues[0].dmConnection, "string");
  for (const records of [player.campaign.quests, player.campaign.threads, player.campaign.clues]) {
    for (const record of records) {
      assert.equal(record.links.locations.every((link) => player.world.locations.some((location) => location.id === link.id)), true);
      assert.equal(record.links.people.every((link) => player.world.people.some((person) => person.id === link.id)), true);
      assert.equal(record.links.factions.every((link) => player.world.factions.some((faction) => faction.id === link.id)), true);
    }
  }
  assert.equal(player.campaign.placesVisited.every((visit) => player.world.locations.some((location) => location.id === visit.location.id)), true);
  assert.equal(player.campaign.facts.find((fact) => fact.label === "Sessions").value, "5");
  assert.equal(dm.campaign.facts.find((fact) => fact.label === "Sessions").value, "6");
});

test("World History is audience filtered and nested entity links cannot reveal secrets", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );
  const playerBeacon = player.world.history.find(
    (event) => event.id === "history.fixture.beacon-blue-flame",
  );

  assert.equal(dm.world.history.length, 9);
  assert.equal(player.world.history.length, 7);
  assert.equal(typeof dm.world.history[0].dmTruth, "string");
  assert.equal(typeof dm.world.history[0].dmConsequence, "string");
  assert.equal(Object.hasOwn(player.world.history[0], "dmTruth"), false);
  assert.equal(Object.hasOwn(player.world.history[0], "dmConsequence"), false);
  assert.equal(Object.hasOwn(player.world.history[0], "playerKnown"), false);
  assert.deepEqual(playerBeacon.linkedLocations, [{ id: "emberwatch", name: "Emberwatch" }]);
  assert.deepEqual(playerBeacon.linkedPeople, [
    { id: "oris-hale", name: "Captain Oris Hale", kind: "NPC" },
  ]);
  assert.equal(
    player.world.history.some((event) => event.id === "history.fixture.cinder-vault-sealed"),
    false,
  );
});

test("location occupants are audience filtered and holdings are DM-only", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );
  const dmArchive = dm.world.locations.find((location) => location.id === "sunken-archive");
  const playerArchive = player.world.locations.find((location) => location.id === "sunken-archive");

  assert.equal(dmArchive.people.length, 3);
  assert.equal(playerArchive.people.length, 2);
  assert.equal(dmArchive.holdings.length, 2);
  assert.equal(Object.hasOwn(playerArchive, "holdings"), false);
  assert.equal(dmArchive.people.some((person) => person.id === "ashbound-curator"), true);
  assert.equal(playerArchive.people.some((person) => person.id === "ashbound-curator"), false);
  assert.equal(typeof dmArchive.people[0].motive, "string");
  assert.equal(typeof dmArchive.people[0].dmSecret, "string");
  assert.equal(Object.hasOwn(playerArchive.people[0], "motive"), false);
  assert.equal(Object.hasOwn(playerArchive.people[0], "dmSecret"), false);
  assert.equal(Object.hasOwn(playerArchive.people[0], "playerKnown"), false);
});

test("People directory is derived exactly from projected location occupants", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );
  const dmOccupantIds = new Set(dm.world.locations.flatMap((location) => location.people.map((person) => person.id)));
  const playerOccupantIds = new Set(
    player.world.locations.flatMap((location) => location.people.map((person) => person.id)),
  );

  assert.equal(dm.world.people.length, 14);
  assert.equal(player.world.people.length, 9);
  assert.deepEqual(new Set(dm.world.people.map((person) => person.id)), dmOccupantIds);
  assert.deepEqual(new Set(player.world.people.map((person) => person.id)), playerOccupantIds);
  assert.equal(player.world.people.every((person) => typeof person.location.name === "string"), true);
  assert.equal(player.world.people.some((person) => person.id === "ashbound-curator"), false);
});

test("Faction and Lore projections filter private records, associations, and nested links", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );
  const playerFox = player.world.factions.find(
    (faction) => faction.id === "faction.fixture.white-fox-company",
  );
  const playerKey = player.world.lore.find((entry) => entry.id === "lore.fixture.bronze-key");

  assert.equal(dm.world.factions.length, 5);
  assert.equal(player.world.factions.length, 4);
  assert.equal(dm.world.lore.length, 10);
  assert.equal(player.world.lore.length, 8);
  assert.equal(typeof dm.world.factions[0].dmAgenda, "string");
  assert.equal(typeof dm.world.lore[0].dmTruth, "string");
  assert.equal(Object.hasOwn(player.world.factions[0], "dmAgenda"), false);
  assert.equal(Object.hasOwn(player.world.lore[0], "dmTruth"), false);
  assert.deepEqual(playerFox.members, [
    { id: "tollhouse-raven", name: "Tollhouse Raven", kind: "Creature" },
  ]);
  assert.deepEqual(playerFox.territories, [
    { id: "greyfen-crossing", name: "Greyfen Crossing", region: "The Ash March" },
  ]);
  assert.deepEqual(playerFox.relationships, [
    { id: "faction.fixture.ash-wardens", name: "The Ash Wardens", stance: "Opposed" },
  ]);
  assert.deepEqual(playerKey.linkedLocations.map(({ id }) => id), ["sunken-archive", "emberwatch"]);
  assert.deepEqual(playerKey.linkedPeople.map(({ id }) => id), ["maelin-quill"]);
  assert.deepEqual(playerKey.linkedFactions.map(({ id }) => id), ["faction.fixture.ash-wardens"]);
  assert.equal(player.world.factions.some((faction) => faction.id === "faction.fixture.drowned-court"), false);
  assert.equal(player.world.lore.some((entry) => entry.id === "lore.fixture.crown-unmaking"), false);
  assert.equal(dm.world.facts.find((fact) => fact.label === "Active factions").value, "5");
  assert.equal(player.world.facts.find((fact) => fact.label === "Active factions").value, "4");
});

test("World map markers and aggregate counts are derived from the projected audience", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );

  assert.equal(dm.world.locations.length, 7);
  assert.equal(player.world.locations.length, 5);
  assert.equal(dm.world.facts.find((fact) => fact.label === "Known places").value, "7");
  assert.equal(player.world.facts.find((fact) => fact.label === "Known places").value, "5");
  assert.equal(dm.world.regions.reduce((total, region) => total + region.count, 0), 7);
  assert.equal(player.world.regions.reduce((total, region) => total + region.count, 0), 5);
  assert.equal(player.world.map.imageUrl, "/world-map-eldervale.png");

  for (const location of dm.world.locations) {
    assert.equal(Number.isFinite(location.mapAnchor.x), true);
    assert.equal(Number.isFinite(location.mapAnchor.y), true);
    assert.equal(location.mapAnchor.x >= 0 && location.mapAnchor.x <= 100, true);
    assert.equal(location.mapAnchor.y >= 0 && location.mapAnchor.y <= 100, true);
  }
});

test("scoped maps expose one hierarchy whose relationships are all explicitly declared", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const byId = new Map(dm.world.maps.map((map) => [map.id, map]));

  assert.equal(dm.world.rootMapId, "map.fixture.world.thalorien");
  assert.equal(byId.get(dm.world.rootMapId).scope, "world");
  assert.deepEqual(
    dm.world.maps.map((map) => map.scope),
    ["world", "world", "region", "region", "region", "city", "city", "city", "location", "location"],
  );

  for (const map of dm.world.maps) {
    // Every map is reachable from the root by declared parent links alone.
    const trail = [];
    let cursor = map;
    while (cursor.parentMapId !== null) {
      trail.unshift(cursor.id);
      cursor = byId.get(cursor.parentMapId);
      assert.notEqual(cursor, undefined);
    }
    // Every map reaches a declared root by parent links alone; the workspace now holds two.
    assert.equal(cursor.parentMapId, null);
    assert.equal(cursor.scope, "world");

    // A scope link is the only relationship carrier, and it always names a projected map.
    for (const link of map.scopeLinks) {
      assert.equal(byId.has(link.childMapId), true);
      assert.equal(byId.get(link.childMapId).parentMapId, map.id);
      if (link.viaFeatureId !== null) {
        assert.equal(map.features.some((feature) => feature.id === link.viaFeatureId), true);
      }
    }
  }
});

test("each scope keeps its own coordinate space and no placement crosses between them", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const spaceIds = dm.world.maps.map((map) => map.coordinateSpace.id);

  assert.equal(new Set(spaceIds).size, spaceIds.length);

  for (const map of dm.world.maps) {
    for (const feature of map.features) {
      assert.equal(feature.coordinateSpaceId, map.coordinateSpace.id);
      assert.equal(feature.geometry.x >= 0 && feature.geometry.x <= map.coordinateSpace.width, true);
      assert.equal(feature.geometry.y >= 0 && feature.geometry.y <= map.coordinateSpace.height, true);
    }
  }

  const region = dm.world.maps.find((map) => map.id === "map.fixture.region.ash-march");
  const world = dm.world.maps.find((map) => map.id === "map.fixture.world.eldervale");
  const regionCrossing = region.features.find((feature) => feature.locationId === "greyfen-crossing");
  const worldCrossing = world.features.find((feature) => feature.locationId === "greyfen-crossing");

  // The same place carries unrelated coordinates in the two scopes; nothing is derived or scaled.
  assert.notDeepEqual(regionCrossing.geometry, worldCrossing.geometry);
  assert.notEqual(region.coordinateSpace.width, world.coordinateSpace.width);
});

test("world scope placement is derived from the projected locations, not a second copy", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const world = dm.world.maps.find((map) => map.id === "map.fixture.world.eldervale");
  const locationById = new Map(dm.world.locations.map((location) => [location.id, location]));

  assert.equal(world.features.length, dm.world.locations.length);
  for (const feature of world.features) {
    const location = locationById.get(feature.locationId);
    assert.notEqual(location, undefined);
    assert.deepEqual(feature.geometry, { x: location.mapAnchor.x, y: location.mapAnchor.y });
    assert.equal(feature.name, location.name);
  }
});

test("a known scope without an approved base stays navigable as information", () => {
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );
  const emberwatch = player.world.maps.find((map) => map.id === "map.fixture.city.emberwatch");

  assert.equal(emberwatch.base, null);
  assert.equal(emberwatch.features.length > 0, true);
  assert.equal(
    player.world.maps.find((map) => map.id === "map.fixture.region.deep-vale").scopeLinks.length,
    0,
  );
});

test("Player map bytes exclude every hidden scope, feature, asset, and child link", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );
  const serialized = JSON.stringify(player);

  assert.equal(dm.world.maps.length, 10);
  assert.equal(player.world.maps.length, 9);
  assert.equal(player.world.maps.some((map) => map.id === "map.fixture.city.blackglass-cove"), false);

  const dmCoast = dm.world.maps.find((map) => map.id === "map.fixture.region.crown-coast");
  const playerCoast = player.world.maps.find((map) => map.id === "map.fixture.region.crown-coast");
  assert.equal(dmCoast.scopeLinks.length, 1);
  assert.equal(playerCoast.scopeLinks.length, 0);
  assert.equal(playerCoast.features.some((feature) => feature.locationId === "blackglass-cove"), false);

  for (const canary of [...HIDDEN_MAP_CANARIES, ...HIDDEN_MAP_FEATURE_CANARIES]) {
    assert.equal(serialized.includes(canary), false, canary);
  }
});

function sourceWithMap(mapId, changes) {
  const clone = structuredClone(hubSource);
  const index = clone.world.maps.findIndex((map) => map.id === mapId);
  clone.world.maps[index] = { ...clone.world.maps[index], ...changes };
  return clone;
}

test("a map layer is emitted only to the audience its declared policy allows", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );
  const dmGreyfen = dm.world.maps.find((map) => map.id === "map.fixture.city.greyfen-crossing");
  const playerGreyfen = player.world.maps.find((map) => map.id === "map.fixture.city.greyfen-crossing");
  const serialized = JSON.stringify(player);

  assert.deepEqual(dmGreyfen.layers.map((layer) => layer.id).slice(-1), [
    "layer.fixture.greyfen.watch-notes",
  ]);
  assert.equal(playerGreyfen.layers.length, dmGreyfen.layers.length - 1);
  assert.equal(playerGreyfen.layers.some((layer) => layer.id.endsWith("watch-notes")), false);

  // The excluded feature is otherwise Player-visible: only its layer's policy removes it.
  const stair = dmGreyfen.features.find((feature) => feature.id.endsWith("unwatched-stair"));
  assert.notEqual(stair, undefined);
  assert.equal(playerGreyfen.features.some((feature) => feature.id === stair.id), false);

  for (const canary of DM_ONLY_LAYER_CANARIES) {
    assert.equal(serialized.includes(canary), false, canary);
  }
  assert.equal(serialized.includes(stair.name), false);
});

test("each audience receives its own base variant and never the other audience's asset", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );
  const serialized = JSON.stringify(player);
  const find = (envelope, id) => envelope.world.maps.find((map) => map.id === id);

  assert.equal(find(player, "map.fixture.region.ash-march").base.imageUrl, "/region-map-ash-march.svg");
  assert.equal(find(dm, "map.fixture.region.ash-march").base.imageUrl, "/region-map-ash-march-dm.svg");

  for (const canary of DM_ONLY_BASE_CANARIES) {
    assert.equal(serialized.includes(canary), false, canary);
  }
});

test("a scope with no base variant for the audience fails closed instead of falling back", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );
  const dmEmberwatch = dm.world.maps.find((map) => map.id === "map.fixture.city.emberwatch");
  const playerEmberwatch = player.world.maps.find((map) => map.id === "map.fixture.city.emberwatch");

  assert.equal(dmEmberwatch.base.imageUrl, "/city-map-emberwatch-dm.svg");
  assert.equal(playerEmberwatch.base, null);
  // The scope stays navigable as information rather than disappearing or borrowing the DM asset.
  assert.equal(playerEmberwatch.features.length > 0, true);
  assert.equal(JSON.stringify(player).includes("/city-map-emberwatch-dm.svg"), false);
});

test("a layer with no usable audience policy is omitted and takes its features with it", () => {
  const unknownPolicy = sourceWithMap("map.fixture.region.deep-vale", {
    layers: [
      { id: "layer.fixture.deep-vale.base", kind: "base", order: 1, label: "Terrain base", audience: "everyone" },
      { id: "layer.fixture.deep-vale.markers", kind: "markers", order: 2, label: "Settlements and sites", audience: "player" },
    ],
  });
  const missingPolicy = sourceWithMap("map.fixture.region.deep-vale", {
    layers: [
      { id: "layer.fixture.deep-vale.markers", kind: "markers", order: 2, label: "Settlements and sites" },
    ],
  });

  for (const seat of ["dm", "player"]) {
    const projected = projectHubEnvelope(
      unknownPolicy,
      HUB_SOURCE_REVISION,
      audience(dmPrincipal, seat),
    ).world.maps.find((map) => map.id === "map.fixture.region.deep-vale");
    assert.deepEqual(projected.layers.map((layer) => layer.id), ["layer.fixture.deep-vale.markers"]);
  }

  const orphaned = projectHubEnvelope(
    missingPolicy,
    HUB_SOURCE_REVISION,
    audience(dmPrincipal, "dm"),
  ).world.maps.find((map) => map.id === "map.fixture.region.deep-vale");
  assert.deepEqual(orphaned.layers, []);
  assert.deepEqual(orphaned.features, []);
});

test("DM Player-preview is byte-equal to a real Player projection for every map", () => {
  const preview = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "player"));
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );

  assert.deepEqual(preview.world.maps, player.world.maps);
  assert.equal(JSON.stringify(preview.world.maps), JSON.stringify(player.world.maps));
});

test("the location scope completes the hierarchy from both of its possible parents", () => {
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );
  const byId = new Map(player.world.maps.map((map) => [map.id, map]));
  const trail = (mapId) => {
    const names = [];
    let cursor = byId.get(mapId);
    while (cursor) {
      names.unshift(cursor.subject.name);
      cursor = cursor.parentMapId === null ? null : byId.get(cursor.parentMapId);
    }
    return names;
  };

  assert.deepEqual(trail("map.fixture.location.sunken-archive"), [
    "Eldervale",
    "The Ash March",
    "The Sunken Archive",
  ]);
  assert.deepEqual(trail("map.fixture.location.tollhouse"), [
    "Eldervale",
    "The Ash March",
    "Greyfen Crossing",
    "The Tollhouse",
  ]);

  // Each location scope is reached by a declared link hanging off a feature that already existed.
  const region = byId.get("map.fixture.region.ash-march");
  const city = byId.get("map.fixture.city.greyfen-crossing");
  const fromRegion = region.scopeLinks.find((link) => link.childScope === "location");
  const fromCity = city.scopeLinks.find((link) => link.childScope === "location");

  assert.equal(fromRegion.viaFeatureId, "feature.fixture.ash-march.sunken-archive");
  assert.equal(fromCity.viaFeatureId, "feature.fixture.greyfen.tollhouse-quay");
  assert.equal(region.features.some((feature) => feature.id === fromRegion.viaFeatureId), true);
  assert.equal(city.features.some((feature) => feature.id === fromCity.viaFeatureId), true);
});

test("being a scope and being a place are independent affordances", () => {
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );
  const byId = new Map(player.world.maps.map((map) => [map.id, map]));
  const region = byId.get("map.fixture.region.ash-march");
  const vale = byId.get("map.fixture.region.deep-vale");

  // The archive is both: an openable location and a navigable scope.
  const archive = region.features.find((feature) => feature.locationId === "sunken-archive");
  assert.equal(region.scopeLinks.some((link) => link.viaFeatureId === archive.id), true);

  // Briar Hollow is a location with no authored view: a place, and only a place.
  const hollow = vale.features.find((feature) => feature.locationId === "briar-hollow");
  assert.notEqual(hollow, undefined);
  assert.equal(vale.scopeLinks.some((link) => link.viaFeatureId === hollow.id), false);
  assert.deepEqual(vale.scopeLinks, []);

  // The tollhouse is a scope with no World location behind it.
  const tollhouse = byId.get("map.fixture.location.tollhouse");
  assert.equal(
    player.world.locations.some((location) => location.id === tollhouse.subject.id),
    false,
  );
});

test("a scene feature is a named point and never carries extent or grid data", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const scenes = dm.world.maps.filter((map) => map.scope === "location");
  const allowedKeys = [
    "coordinateSpaceId",
    "detail",
    "geometry",
    "id",
    "kind",
    "layerId",
    "locationId",
    "name",
  ];

  assert.equal(scenes.length, 2);
  for (const scene of scenes) {
    assert.equal(scene.features.length > 0, true);
    for (const feature of scene.features) {
      assert.equal(feature.kind, "point");
      assert.deepEqual(Object.keys(feature).sort(), allowedKeys);
      assert.deepEqual(Object.keys(feature.geometry).sort(), ["x", "y"]);
    }
  }
});

test("audience policy holds four levels deep at the location scope", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );
  const find = (envelope) =>
    envelope.world.maps.find((map) => map.id === "map.fixture.location.sunken-archive");
  const serialized = JSON.stringify(player);

  assert.equal(find(dm).layers.some((layer) => layer.id.endsWith("ward-notes")), true);
  assert.equal(find(player).layers.some((layer) => layer.id.endsWith("ward-notes")), false);
  assert.equal(find(dm).features.some((feature) => feature.id.endsWith("brass-door")), true);
  assert.equal(find(player).features.some((feature) => feature.id.endsWith("brass-door")), false);

  assert.equal(find(player).base.imageUrl, "/location-map-sunken-archive.svg");
  assert.equal(find(dm).base.imageUrl, "/location-map-sunken-archive-dm.svg");
  assert.equal(serialized.includes("/location-map-sunken-archive-dm.svg"), false);
  assert.equal(serialized.includes("The sealed brass door"), false);
  assert.equal(serialized.includes("Ward notes"), false);
});

test("campaign overlays annotate World maps and change no geography whatsoever", () => {
  const withOverlays = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience(dmPrincipal, "dm"),
  );
  const stripped = structuredClone(hubSource);
  stripped.campaign.mapOverlays = [];
  const withoutOverlays = projectHubEnvelope(
    stripped,
    HUB_SOURCE_REVISION,
    audience(dmPrincipal, "dm"),
  );

  assert.equal(withOverlays.campaign.mapOverlays.length > 0, true);
  assert.deepEqual(withoutOverlays.campaign.mapOverlays, []);

  // Deleting every campaign annotation leaves the World maps byte-identical.
  assert.equal(
    JSON.stringify(withOverlays.world.maps),
    JSON.stringify(withoutOverlays.world.maps),
  );

  // Overlays live under campaign, and carry no geography of their own.
  assert.equal(withOverlays.world.mapOverlays, undefined);
  for (const overlay of withOverlays.campaign.mapOverlays) {
    assert.deepEqual(Object.keys(overlay).sort(), [
      "detail",
      "featureId",
      "id",
      "kind",
      "label",
      "mapId",
      "recordedOn",
    ]);
  }
});

test("an overlay is dropped when the audience cannot reach its target, and leaves no trace", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );
  const serialized = JSON.stringify(player);
  const ids = (envelope) => envelope.campaign.mapOverlays.map((overlay) => overlay.id);

  assert.equal(dm.campaign.mapOverlays.length, 7);
  assert.equal(player.campaign.mapOverlays.length, 4);

  // Dropped because the overlay itself is DM-only.
  assert.equal(ids(player).includes("overlay.fixture.vault-approach"), false);
  // Dropped because its map is hidden from Player.
  assert.equal(ids(player).includes("overlay.fixture.cove-invitation"), false);
  // Dropped although the overlay is Player-visible: its target feature is on a DM-only layer.
  assert.equal(ids(dm).includes("overlay.fixture.archive-brass-door"), true);
  assert.equal(ids(player).includes("overlay.fixture.archive-brass-door"), false);

  for (const canary of [...HIDDEN_OVERLAY_CANARIES, ...UNREACHABLE_OVERLAY_CANARIES]) {
    assert.equal(serialized.includes(canary), false, canary);
  }

  // Every surviving overlay still points at something this audience can actually see.
  const mapById = new Map(player.world.maps.map((map) => [map.id, map]));
  for (const overlay of player.campaign.mapOverlays) {
    const map = mapById.get(overlay.mapId);
    assert.notEqual(map, undefined);
    if (overlay.featureId !== null) {
      assert.equal(map.features.some((feature) => feature.id === overlay.featureId), true);
    }
  }
});

test("overlay order is deterministic and a DM Player-preview matches a real Player", () => {
  const first = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const second = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const preview = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "player"));
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );

  assert.deepEqual(first.campaign.mapOverlays, second.campaign.mapOverlays);
  assert.deepEqual(preview.campaign.mapOverlays, player.campaign.mapOverlays);

  // Two overlays on the same map both survive, in a stable order.
  const archive = player.campaign.mapOverlays.filter(
    (overlay) => overlay.mapId === "map.fixture.location.sunken-archive",
  );
  assert.deepEqual(archive.map((overlay) => overlay.id), [
    "overlay.fixture.archive-ward",
    "overlay.fixture.archive-nave",
  ]);
});

test("map projection is deterministic and mutates no authored source record", () => {
  const before = JSON.stringify(hubSource.world.maps);
  const first = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const second = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));

  assert.deepEqual(first.world.maps, second.world.maps);
  assert.equal(JSON.stringify(hubSource.world.maps), before);

  first.world.maps[0].features.push({ id: "feature.injected" });
  assert.equal(JSON.stringify(hubSource.world.maps), before);
});

test("DM can read DM data or preview the exact Player information projection", () => {
  const dm = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "dm"));
  const preview = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience(dmPrincipal, "player"));
  const player = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    audience("principal.player.fixture", "player"),
  );

  assert.equal(dm.world.locations[0].dmSecret, SECRET_CANARIES[0]);
  assert.deepEqual(preview.world, player.world);
  assert.deepEqual(preview.campaign, player.campaign);
  assert.deepEqual(preview.party, player.party);
  assert.deepEqual(preview.rules, player.rules);
});

test("the closed envelope never returns principal identity and denied reads return no data", () => {
  const ready = readHubEnvelope({
    authenticatedUserId: dmPrincipal,
    authenticatedUserEmail: "keeper@example.test",
    requestedPerspective: "dm",
    environment: {
      NODE_ENV: "production",
      DND2024_DM_USER_IDS: dmPrincipal,
      DND2024_DM_EMAILS: "keeper@example.test",
    },
  });
  const denied = readHubEnvelope({
    authenticatedUserId: "",
    authenticatedUserEmail: "",
    requestedPerspective: "dm",
    environment: { NODE_ENV: "production", DND2024_DM_USER_IDS: dmPrincipal },
  });

  assert.equal(JSON.stringify(ready).includes(dmPrincipal), false);
  assert.equal(JSON.stringify(ready).includes("keeper@example.test"), false);
  assert.deepEqual(Object.keys(denied).sort(), ["message", "status", "version"]);
});
