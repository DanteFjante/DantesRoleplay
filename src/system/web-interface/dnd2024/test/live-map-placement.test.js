import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";

import { connectedCampaignToHubEnvelope } from "../src/server/connected-hub-envelope.ts";
import { isReadyHubEnvelope, resolveMapNavigation } from "../src/state.js";

const CAMPAIGN = {
  id: "campaign.thalorien.brackenford",
  name: "The Waystone at Brackenford",
  premise: "A frontier village on the Greenmantle edge.",
  partyGoals: [],
  toneAndBoundaries: [],
};

function visual(assetKey, alt) {
  return {
    imageUrl: `/api/applications/dnd2024/state-spaces/space.test/entities/${assetKey}/media/map/content`,
    alt,
  };
}

function directory(perspective) {
  const suffix = perspective === "dm" ? "dm" : "player";
  return [
    {
      id: "location.thalorien.thalos",
      name: "Thalos",
      kind: "region",
      containerId: "world.thalorien",
      containmentSlot: "region",
      mapVisual: visual(`thalos.${suffix}`, `${perspective} Thalos map`),
    },
    {
      id: "location.thalorien.aldros",
      name: "Aldros",
      kind: "region",
      containerId: "location.thalorien.thalos",
      containmentSlot: "region",
      mapAnchor: { x: 500, y: 407 },
      mapVisual: visual(`thalos.region.aldros.${suffix}`, `${perspective} Aldros map`),
    },
    {
      id: "location.thalorien.valeros",
      name: "Valeros",
      kind: "region",
      containerId: "location.thalorien.thalos",
      containmentSlot: "region",
      mapAnchor: { x: 700, y: 667 },
      mapVisual: visual(`thalos.region.valeros.${suffix}`, `${perspective} Valeros map`),
    },
    {
      id: "location.thalorien.crownmere",
      name: "Crownmere",
      kind: "settlement",
      containerId: "location.thalorien.aldros",
      containmentSlot: "location",
      mapAnchor: { x: 692, y: 516 },
      mapVisual: visual(`thalos.city.crownmere.${suffix}`, `${perspective} Crownmere map`),
    },
    {
      id: "location.thalorien.brackenford",
      name: "Brackenford",
      kind: "settlement",
      summary: "A frontier village.",
      containerId: "location.thalorien.valeros",
      containmentSlot: "location",
      mapAnchor: { x: 232, y: 647 },
    },
  ];
}

function connected(perspective, entries = directory(perspective), campaign = CAMPAIGN) {
  const world = campaign.id === "campaign.caldris.measure-of-mercy"
    ? { id: "world.caldris", name: "Caldris" }
    : { id: "world.thalorien", name: "Thalorien" };
  return {
    version: 1,
    status: "connected",
    applicationId: "dnd2024",
    stateSpaceId: "space.test",
    contextSelection: {
      selectedWorldId: world.id,
      selectedCampaignId: campaign.id,
      worlds: [{
        ...world,
        campaigns: [{ id: campaign.id, name: campaign.name }],
      }],
    },
    audience: perspective === "dm"
      ? { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] }
      : { seat: "player", perspective: "player", allowedPerspectives: ["player"] },
    campaign,
    actor: { id: "actor.test", name: "Tester", state: null, entries: [] },
    knowledge: {
      status: "ready",
      entries: [],
      locations: [{
        name: "Brackenford",
        entries: [{ text: "The party knows the old well.", stance: "known", presentationKind: "statement" }],
      }],
    },
    chronology: { status: "empty", perspective, entries: [] },
    locationDirectoryAudience: perspective,
    locationDirectory: entries,
  };
}

function mapFor(envelope, locationId) {
  return envelope.world.maps.find((map) => map.subject.id === locationId) ?? null;
}

function featureFor(map, locationId) {
  return map?.features.find((feature) => feature.locationId === locationId) ?? null;
}

test("a marker-only village opens its actual parent map and exact existing marker", () => {
  const envelope = connectedCampaignToHubEnvelope(connected("dm"));
  const parent = mapFor(envelope, "location.thalorien.valeros");
  const village = mapFor(envelope, "location.thalorien.brackenford");
  const before = JSON.stringify(envelope.world.maps);
  assert.deepEqual(resolveMapNavigation(envelope.world.maps, village.id), {
    mapId: parent.id,
    featureId: featureFor(parent, village.subject.id).id,
  });
  assert.equal(JSON.stringify(envelope.world.maps), before);
});

test("failed media lookup retains its scope, and absent unanchored places never borrow coordinates", () => {
  const envelope = connectedCampaignToHubEnvelope(connected("dm"));
  const parent = mapFor(envelope, "location.thalorien.valeros");
  const village = mapFor(envelope, "location.thalorien.brackenford");
  village.baseState = "unavailable";
  assert.deepEqual(resolveMapNavigation(envelope.world.maps, village.id), { mapId: village.id, featureId: "" });
  village.baseState = "absent";
  parent.features = [];
  assert.deepEqual(resolveMapNavigation(envelope.world.maps, village.id), { mapId: parent.id, featureId: "" });
});

test("live anchors keep the same location stable between DM and Player projections", () => {
  const dm = connectedCampaignToHubEnvelope(connected("dm"));
  const player = connectedCampaignToHubEnvelope(connected("player"));
  assert.deepEqual(
    featureFor(mapFor(player, "location.thalorien.valeros"), "location.thalorien.brackenford")?.geometry,
    featureFor(mapFor(dm, "location.thalorien.valeros"), "location.thalorien.brackenford")?.geometry,
  );
  assert.deepEqual(
    featureFor(mapFor(dm, "location.thalorien.valeros"), "location.thalorien.brackenford")?.geometry,
    { x: 232, y: 647 },
  );
});

test("live maps declare one canonical frame and preserve exact parent anchor identity", () => {
  const envelope = connectedCampaignToHubEnvelope(connected("dm"));
  for (const map of envelope.world.maps) {
    assert.deepEqual(map.coordinateSpace, {
      id: `space.live.${map.subject.id}`,
      unit: "normalized",
      width: 1000,
      height: 1000,
      frame: { origin: "top-left", xAxis: "right", yAxis: "down", orientation: "north-up" },
    });
    for (const feature of map.features) {
      assert.equal(feature.coordinateSpaceId, map.coordinateSpace.id);
      assert.ok(feature.geometry.x >= 0 && feature.geometry.x <= 1000);
      assert.ok(feature.geometry.y >= 0 && feature.geometry.y <= 1000);
    }
    for (const link of map.scopeLinks) {
      const child = envelope.world.maps.find((candidate) => candidate.id === link.childMapId);
      assert.equal(child?.parentMapId, map.id);
      if (link.viaFeatureId === null) {
        assert.equal(link.parentAnchor, null);
        continue;
      }
      const feature = map.features.find((candidate) => candidate.id === link.viaFeatureId);
      assert.deepEqual(link.parentAnchor, {
        coordinateSpaceId: map.coordinateSpace.id,
        featureId: feature.id,
        geometry: feature.geometry,
      });
    }
  }
});

test("adding and renaming locations updates data overlays without changing raster identity", () => {
  const initialEntries = directory("dm");
  const initial = connectedCampaignToHubEnvelope(connected("dm", initialEntries));
  const changedEntries = directory("dm").map((entry) => entry.id === "location.thalorien.brackenford"
    ? { ...entry, name: "Brackenford Crossing" }
    : entry);
  changedEntries.push({
    id: "location.thalorien.bell-tower",
    name: "North Bell Tower",
    kind: "site",
    summary: "A watch site above the road.",
    containerId: "location.thalorien.valeros",
    containmentSlot: "location",
    mapAnchor: { x: 410, y: 335 },
  });
  const changed = connectedCampaignToHubEnvelope(connected("dm", changedEntries));
  const initialParent = mapFor(initial, "location.thalorien.valeros");
  const changedParent = mapFor(changed, "location.thalorien.valeros");
  const rasterIdentity = (map) => createHash("sha256").update(JSON.stringify(map.base)).digest("hex");

  assert.equal(rasterIdentity(changedParent), rasterIdentity(initialParent));
  assert.equal(featureFor(changedParent, "location.thalorien.brackenford")?.name, "Brackenford Crossing");
  assert.deepEqual(featureFor(changedParent, "location.thalorien.bell-tower"), {
    id: "feature.live.location.thalorien.valeros.location.thalorien.bell-tower",
    kind: "point",
    layerId: "layer.live.region.sites",
    coordinateSpaceId: "space.live.location.thalorien.valeros",
    geometry: { x: 410, y: 335 },
    icon: "site",
    name: "North Bell Tower",
    detail: "A watch site above the road.",
    locationId: "location.thalorien.bell-tower",
  });
});

test("site and interior maps render direct child labels from anchors without baking names into rasters", () => {
  const entries = [
    { id: "location.detail.region", name: "Detail Region", kind: "region",
      containerId: "world.thalorien", containmentSlot: "region",
      mapVisual: visual("detail.region", "Region map") },
    { id: "location.detail.site", name: "Mallow Abbey", kind: "site",
      containerId: "location.detail.region", containmentSlot: "location", mapAnchor: { x: 500, y: 500 },
      mapVisual: visual("detail.site", "Abbey map") },
    { id: "location.detail.room", name: "Chapter House", kind: "interior",
      summary: "A vaulted gathering room.", containerId: "location.detail.site",
      containmentSlot: "location", mapAnchor: { x: 640, y: 370 },
      mapVisual: visual("detail.room", "Room map") },
    { id: "location.detail.lectern", name: "Oak Lectern", kind: "site",
      summary: "A carved lectern.", containerId: "location.detail.room",
      containmentSlot: "location", mapAnchor: { x: 260, y: 720 } },
  ];
  const initial = connectedCampaignToHubEnvelope(connected("dm", entries));
  const renamed = connectedCampaignToHubEnvelope(connected("dm", entries.map((entry) =>
    entry.id === "location.detail.room" ? { ...entry, name: "Refectory" } : entry)));
  const siteMap = mapFor(initial, "location.detail.site");
  const renamedMap = mapFor(renamed, "location.detail.site");
  const interiorMap = mapFor(initial, "location.detail.room");
  const rasterIdentity = (map) => createHash("sha256").update(JSON.stringify(map.base)).digest("hex");

  assert.ok(isReadyHubEnvelope(initial));
  assert.equal(featureFor(siteMap, "location.detail.room")?.name, "Chapter House");
  assert.deepEqual(featureFor(siteMap, "location.detail.room")?.geometry, { x: 640, y: 370 });
  assert.equal(featureFor(interiorMap, "location.detail.lectern")?.name, "Oak Lectern");
  assert.deepEqual(featureFor(interiorMap, "location.detail.lectern")?.geometry, { x: 260, y: 720 });
  assert.equal(featureFor(renamedMap, "location.detail.room")?.name, "Refectory");
  assert.equal(rasterIdentity(renamedMap), rasterIdentity(siteMap));
});

test("Thalos is the main map and uses authorized media for both perspectives", () => {
  const dm = connectedCampaignToHubEnvelope(connected("dm"));
  const player = connectedCampaignToHubEnvelope(connected("player"));
  assert.equal(dm.world.rootMapId, "map.live.location.thalorien.thalos");
  assert.equal(player.world.rootMapId, "map.live.location.thalorien.thalos");
  assert.equal(mapFor(dm, "location.thalorien.thalos")?.base?.imageUrl, visual("thalos.dm", "").imageUrl);
  assert.equal(mapFor(player, "location.thalorien.thalos")?.base?.imageUrl, visual("thalos.player", "").imageUrl);
  assert.equal(JSON.stringify(player).includes("thalos-map-dm.svg"), false);
});

test("Caldris map owners resolve only to authorized World and Eredane media", () => {
  const entries = [
    {
      id: "location.caldris.eredane",
      name: "Eredane",
      kind: "region",
      containerId: "world.caldris",
      containmentSlot: "region",
      mapAnchor: { x: 310, y: 510 },
      mapVisual: visual("caldris.world.player", "Caldris world map"),
    },
    {
      id: "location.caldris.bramblebridge",
      name: "Bramblebridge",
      kind: "settlement",
      containerId: "location.caldris.eredane",
      containmentSlot: "location",
      mapAnchor: { x: 540, y: 520 },
      mapVisual: visual("caldris.region.eredane.player", "Eredane regional map"),
    },
    {
      id: "location.caldris.gilded-kettle",
      name: "The Gilded Kettle",
      kind: "interior",
      containerId: "location.caldris.bramblebridge",
      containmentSlot: "location",
      mapAnchor: { x: 430, y: 610 },
      mapVisual: visual("caldris.town.bramblebridge.player", "Bramblebridge town map"),
    },
  ];
  const envelope = connectedCampaignToHubEnvelope(connected("player", entries,
    { ...CAMPAIGN, id: "campaign.caldris.measure-of-mercy", name: "The Measure of Mercy" }));

  assert.equal(mapFor(envelope, "location.caldris.eredane")?.base?.imageUrl,
    visual("caldris.world.player", "").imageUrl);
  assert.equal(mapFor(envelope, "location.caldris.bramblebridge")?.base?.imageUrl,
    visual("caldris.region.eredane.player", "").imageUrl);
  assert.equal(mapFor(envelope, "location.caldris.gilded-kettle")?.base?.imageUrl,
    visual("caldris.town.bramblebridge.player", "").imageUrl);
});

test("Caldris resolves its 2000×1500 atlas below the non-map World and keeps exact top-level anchors", () => {
  const atlasVisual = {
    ...visual("caldris.atlas.player", "Caldris atlas"),
    width: 2000,
    height: 1500,
  };
  const entries = [
    {
      id: "world.caldris", name: "Caldris", kind: "world", isWorldRoot: true,
      mapVisualState: "absent",
    },
    {
      id: "location.caldris.atlas", name: "Caldris", kind: "region",
      containerId: "world.caldris", containmentSlot: "atlas", mapVisualState: "ready",
      mapVisual: atlasVisual,
    },
    {
      id: "location.caldris.eredane", name: "Eredane", kind: "region",
      containerId: "location.caldris.atlas", mapAnchor: { x: 255, y: 380 },
      mapVisualState: "absent",
    },
    {
      id: "location.caldris.atlas.lantern-sea", name: "Lantern Sea", kind: "region",
      containerId: "location.caldris.atlas", mapAnchor: { x: 510, y: 570 },
      mapVisualState: "absent",
    },
    {
      id: "location.caldris.solasca", name: "Solasca", kind: "region",
      containerId: "location.caldris.atlas", mapAnchor: { x: 765, y: 490 },
      mapVisualState: "absent",
    },
  ];
  const envelope = connectedCampaignToHubEnvelope(connected("player", entries,
    { ...CAMPAIGN, id: "campaign.caldris.measure-of-mercy", name: "The Measure of Mercy" }));
  const atlas = mapFor(envelope, "location.caldris.atlas");

  assert.equal(envelope.world.mapOwnerId, "location.caldris.atlas");
  assert.equal(envelope.world.rootMapId, "map.live.location.caldris.atlas");
  assert.deepEqual(atlas?.base, atlasVisual);
  assert.deepEqual(atlas?.features.map(({ name, geometry }) => ({ name, geometry })), [
    { name: "Eredane", geometry: { x: 255, y: 380 } },
    { name: "Lantern Sea", geometry: { x: 510, y: 570 } },
    { name: "Solasca", geometry: { x: 765, y: 490 } },
  ]);
  assert.equal(isReadyHubEnvelope(envelope), true);
});

test("a declared atlas remains the map owner when its media lookup is unavailable", () => {
  const entries = [
    {
      id: "world.caldris", name: "Caldris", kind: "world", isWorldRoot: true,
      mapVisualState: "absent",
    },
    {
      id: "location.caldris.atlas", name: "Caldris Atlas", kind: "region",
      containerId: "world.caldris", containmentSlot: "atlas", mapVisualState: "unavailable",
    },
    {
      id: "location.caldris.eredane", name: "Eredane", kind: "region",
      containerId: "location.caldris.atlas", mapAnchor: { x: 255, y: 380 },
      mapVisualState: "absent",
    },
  ];
  const envelope = connectedCampaignToHubEnvelope(connected("player", entries,
    { ...CAMPAIGN, id: "campaign.caldris.measure-of-mercy", name: "The Measure of Mercy" }));
  const atlas = mapFor(envelope, "location.caldris.atlas");

  assert.equal(envelope.world.mapOwnerId, "location.caldris.atlas");
  assert.equal(envelope.world.rootMapId, "map.live.location.caldris.atlas");
  assert.equal(atlas?.baseState, "unavailable");
  assert.equal(atlas?.base, null);
  assert.deepEqual(atlas?.features.map(({ name, geometry }) => ({ name, geometry })), [
    { name: "Eredane", geometry: { x: 255, y: 380 } },
  ]);
  assert.equal(isReadyHubEnvelope(envelope), true);
});

test("server page bundles preserve owner-bound media routes", () => {
  const envelope = connectedCampaignToHubEnvelope(connected("player"));
  assert.equal(
    mapFor(envelope, "location.thalorien.thalos")?.base?.imageUrl,
    visual("thalos.player", "").imageUrl,
  );
  assert.equal(
    mapFor(envelope, "location.thalorien.crownmere")?.base?.imageUrl,
    visual("thalos.city.crownmere.player", "").imageUrl,
  );
});

test("containment builds Thalos, region, and city scopes without an entity-id map table", () => {
  const envelope = connectedCampaignToHubEnvelope(connected("dm"));
  const thalos = mapFor(envelope, "location.thalorien.thalos");
  const aldros = mapFor(envelope, "location.thalorien.aldros");
  const crownmere = mapFor(envelope, "location.thalorien.crownmere");
  assert.equal(thalos?.scope, "world");
  assert.equal(aldros?.scope, "region");
  assert.equal(aldros?.base?.imageUrl, visual("thalos.region.aldros.dm", "").imageUrl);
  assert.equal(aldros?.parentMapId, thalos?.id);
  assert.equal(crownmere?.scope, "city");
  assert.equal(crownmere?.parentMapId, aldros?.id);
  assert.equal(crownmere?.base?.imageUrl, visual("thalos.city.crownmere.dm", "").imageUrl);
  assert.equal(thalos?.scopeLinks.some((link) => link.childMapId === aldros?.id), true);
  assert.equal(aldros?.scopeLinks.some((link) => link.childMapId === crownmere?.id), true);
  assert.equal(isReadyHubEnvelope(envelope), true);
});

test("a new live city can reuse authorized media without adding its entity id to code", () => {
  const entries = directory("dm");
  entries.push({
    id: "location.thalorien.new-port",
    name: "New Port",
    kind: "settlement",
    containerId: "location.thalorien.valeros",
    containmentSlot: "location",
    mapAnchor: { x: 410, y: 580 },
    mapVisual: visual("thalos.city.merrowgate.dm", "A reviewed map used for New Port."),
  });
  const envelope = connectedCampaignToHubEnvelope(connected("dm", entries));
  const city = mapFor(envelope, "location.thalorien.new-port");
  assert.equal(city?.parentMapId, mapFor(envelope, "location.thalorien.valeros")?.id);
  assert.equal(city?.base?.imageUrl, visual("thalos.city.merrowgate.dm", "").imageUrl);
});

test("unknown media keys fail closed while the location information remains", () => {
  const entries = directory("player");
  const crownmere = entries.find((entry) => entry.id === "location.thalorien.crownmere");
  crownmere.mapVisual = { imageUrl: "/components/maps/unknown.png", alt: "CANARY UNKNOWN MAP" };
  const envelope = connectedCampaignToHubEnvelope(connected("player", entries));
  assert.equal(mapFor(envelope, "location.thalorien.crownmere")?.baseState, "unavailable");
  assert.equal(mapFor(envelope, "location.thalorien.crownmere")?.base, null);
  assert.equal(envelope.world.locations.some((location) => location.id === crownmere.id), true);
  assert.equal(JSON.stringify(envelope).includes("unknown.secret.player"), false);
  assert.equal(JSON.stringify(envelope).includes("CANARY UNKNOWN MAP"), false);
});

test("an unanchored child is omitted from maps rather than assigned an invented point", () => {
  const entries = directory("dm");
  entries.push({
    id: "location.thalorien.unplaced",
    name: "Nowhere Yet",
    kind: "site",
    containerId: "location.thalorien.valeros",
    containmentSlot: "location",
  });
  const envelope = connectedCampaignToHubEnvelope(connected("dm", entries));
  const parent = mapFor(envelope, "location.thalorien.valeros");
  assert.equal(featureFor(parent, "location.thalorien.unplaced"), null);
  assert.deepEqual(parent?.scopeLinks.find((link) => link.childName === "Nowhere Yet"), {
    id: "scopelink.live.location.thalorien.valeros.location.thalorien.unplaced",
    childMapId: "map.live.location.thalorien.unplaced",
    childScope: "location",
    childName: "Nowhere Yet",
    viaFeatureId: null,
    parentAnchor: null,
  });
  assert.equal(mapFor(envelope, "location.thalorien.unplaced")?.baseState, "absent");
  assert.equal(envelope.world.locations.some((location) => location.id === "location.thalorien.unplaced"), true);
});
