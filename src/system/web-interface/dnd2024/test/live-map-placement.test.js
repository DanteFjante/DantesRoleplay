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
  return { assetKey, alt };
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

function connected(perspective, entries = directory(perspective)) {
  return {
    version: 1,
    status: "connected",
    applicationId: "dnd2024",
    stateSpaceId: "space.test",
    audience: perspective === "dm"
      ? { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] }
      : { seat: "player", perspective: "player", allowedPerspectives: ["player"] },
    campaign: CAMPAIGN,
    actor: { id: "actor.test", name: "Tester", state: null, entries: [] },
    knowledge: {
      status: "ready",
      entries: [],
      locations: [{
        name: "Brackenford",
        entries: [{ text: "The party knows the old well.", stance: "known", presentationKind: "statement" }],
      }],
    },
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

test("Thalos is the main map and uses the reviewed public atlas for both perspectives", () => {
  const dm = connectedCampaignToHubEnvelope(connected("dm"));
  const player = connectedCampaignToHubEnvelope(connected("player"));
  assert.equal(dm.world.rootMapId, "map.live.location.thalorien.thalos");
  assert.equal(player.world.rootMapId, "map.live.location.thalorien.thalos");
  assert.equal(mapFor(dm, "location.thalorien.thalos")?.base?.imageUrl, "/components/maps/thalos-world.png");
  assert.equal(mapFor(player, "location.thalorien.thalos")?.base?.imageUrl, "/components/maps/thalos-world.png");
  assert.equal(JSON.stringify(player).includes("thalos-map-dm.svg"), false);
});

test("server page bundles keep host-served atlas routes while relocating page-owned city maps", () => {
  const envelope = connectedCampaignToHubEnvelope(connected("player"), {
    assetBaseUrl: "/ui/dnd2024-play/assets/",
  });
  assert.equal(
    mapFor(envelope, "location.thalorien.thalos")?.base?.imageUrl,
    "/components/maps/thalos-world.png",
  );
  assert.equal(
    mapFor(envelope, "location.thalorien.crownmere")?.base?.imageUrl,
    "/ui/dnd2024-play/assets/city-map-crownmere-v2.png",
  );
});

test("containment builds Thalos, region, and city scopes without an entity-id map table", () => {
  const envelope = connectedCampaignToHubEnvelope(connected("dm"));
  const thalos = mapFor(envelope, "location.thalorien.thalos");
  const aldros = mapFor(envelope, "location.thalorien.aldros");
  const crownmere = mapFor(envelope, "location.thalorien.crownmere");
  assert.equal(thalos?.scope, "world");
  assert.equal(aldros?.scope, "region");
  assert.equal(aldros?.base?.imageUrl, "/components/maps/region-aldros.png");
  assert.equal(aldros?.parentMapId, thalos?.id);
  assert.equal(crownmere?.scope, "city");
  assert.equal(crownmere?.parentMapId, aldros?.id);
  assert.equal(crownmere?.base?.imageUrl, "/city-map-crownmere-v2.png");
  assert.equal(thalos?.scopeLinks.some((link) => link.childMapId === aldros?.id), true);
  assert.equal(aldros?.scopeLinks.some((link) => link.childMapId === crownmere?.id), true);
  assert.equal(isReadyHubEnvelope(envelope), true);
});

test("a new live city can reuse reviewed bytes without adding its entity id to code", () => {
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
  assert.equal(city?.base?.imageUrl, "/city-map-merrowgate-v2.png");
});

test("unknown media keys fail closed while the location information remains", () => {
  const entries = directory("player");
  const crownmere = entries.find((entry) => entry.id === "location.thalorien.crownmere");
  crownmere.mapVisual = visual("unknown.secret.player", "CANARY UNKNOWN MAP");
  const envelope = connectedCampaignToHubEnvelope(connected("player", entries));
  assert.equal(mapFor(envelope, "location.thalorien.crownmere"), null);
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
  assert.equal(featureFor(mapFor(envelope, "location.thalorien.valeros"), "location.thalorien.unplaced"), null);
  assert.equal(envelope.world.locations.some((location) => location.id === "location.thalorien.unplaced"), true);
});
