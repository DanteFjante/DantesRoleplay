import assert from "node:assert/strict";
import test from "node:test";

import { connectedCampaignToHubEnvelope } from "../src/server/connected-hub-envelope.ts";
import { isReadyHubEnvelope } from "../src/state.js";

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
  assert.equal(parent?.scopeLinks.some((link) => link.childName === "Nowhere Yet"), false);
  assert.equal(mapFor(envelope, "location.thalorien.unplaced"), null);
  assert.equal(envelope.world.locations.some((location) => location.id === "location.thalorien.unplaced"), true);
});
