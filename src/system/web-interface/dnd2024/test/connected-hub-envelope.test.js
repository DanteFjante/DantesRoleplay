import assert from "node:assert/strict";
import test from "node:test";

import { connectedCampaignToHubEnvelope } from "../src/server/connected-hub-envelope.ts";
import { isReadyHubEnvelope } from "../src/state.js";

function connectedFixture({
  knowledgeStatus = "ready",
  knowledgeEntries = [{ text: "A placeholder campaign fact.", stance: "known", presentationKind: "statement" }],
  locations = [],
  audience,
  locationDirectoryAudience,
  locationDirectory,
  worldDirectory,
  chapters = [],
  arcs = [],
  sessions = [],
  currentLocationId,
  rules,
  party,
} = {}) {
  return {
    version: 1,
    status: "connected",
    applicationId: "dnd2024",
    stateSpaceId: "dnd2024-main",
    ...(currentLocationId ? { currentLocationId } : {}),
    audience: audience ?? { seat: "dm", perspective: "player", allowedPerspectives: ["dm", "player"] },
    campaign: {
      id: "campaign.thalorien.brackenford",
      name: "The Waystone at Brackenford",
      status: "active",
      premise: null,
      partyGoals: ["Build trust with the people of Brackenford."],
      toneAndBoundaries: [],
      chapters,
      arcs,
      sessions,
    },
    actor: { id: "orban", name: "Orban", state: null, entries: [] },
    ...(party ? { party } : {}),
    knowledge: {
      status: knowledgeStatus,
      entries: knowledgeEntries,
      locations,
    },
    ...(locationDirectoryAudience ? { locationDirectoryAudience } : {}),
    ...(locationDirectory ? { locationDirectory } : {}),
    ...(worldDirectory ? { worldDirectory } : {}),
    ...(rules ? { rules } : {}),
  };
}

test("projects party records into distinct dossier sections without inventing sheet values", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "player", allowedPerspectives: ["player"] },
    knowledgeEntries: [{ text: "The old road is watched.", stance: "known", presentationKind: "statement" }],
    party: [{
      id: "actor.thalorien.brackenford.orban",
      name: "Orban",
      state: "active",
      current: true,
      entries: [
        { kind: "class", key: "bard", label: "Provisional Bard direction", details: "A musical character direction." },
        { kind: "background", key: "troupe", label: "Raised in a traveling troupe", details: "A communal performing childhood." },
        { kind: "note", key: "nara", label: "Nara", details: "His closest friend." },
        { kind: "equipment", key: "ocarina", label: "Blue metal ocarina", details: "An inherited instrument." },
      ],
    }],
  }));

  assert.equal(envelope.party[0].id, "actor.thalorien.brackenford.orban");
  assert.equal(envelope.party[0].recordStatus, "Provisional character record");
  assert.deepEqual(envelope.party[0].sheet.map((entry) => entry.title), ["Provisional Bard direction"]);
  assert.deepEqual(envelope.party[0].origin.map((entry) => entry.title), [
    "Provisional Bard direction",
    "Raised in a traveling troupe",
  ]);
  assert.deepEqual(envelope.party[0].backstory.map((entry) => entry.title), [
    "Raised in a traveling troupe",
    "Nara",
  ]);
  assert.deepEqual(envelope.party[0].inventory.map((entry) => entry.title), ["Blue metal ocarina"]);
  assert.deepEqual(envelope.party[0].knowledge.map((entry) => entry.text), ["The old road is watched."]);
  assert.equal(JSON.stringify(envelope.party).includes("armor class"), false);
});

test("does not attach DM knowledge to character dossiers", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    knowledgeEntries: [{ text: "CANARY DM KNOWLEDGE", stance: "known", presentationKind: "statement" }],
    party: [{ id: "actor.one", name: "One", state: "active", current: false, entries: [] }],
  }));

  assert.deepEqual(envelope.party[0].knowledge, []);
  assert.equal(JSON.stringify(envelope.party).includes("CANARY DM KNOWLEDGE"), false);
});

test("projects the same closed rules reference supplied by the catalog reader", () => {
  const rules = [{
    id: "dnd2024.shared.action.search",
    title: "Search",
    category: "Action",
    summary: "Make a specified Wisdom check to find or discern something.",
    source: {
      id: "source.dnd2024.srd-5.2.1",
      locator: "Playing the Game > Actions > Search (SRD 5.2.1, pages 10-10)",
    },
  }];
  const dm = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    rules,
  }));
  const preview = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "player", allowedPerspectives: ["dm", "player"] },
    rules,
  }));

  assert.deepEqual(dm.rules, rules);
  assert.deepEqual(preview.rules, rules);
  assert.equal(isReadyHubEnvelope(dm), true);
  assert.equal(isReadyHubEnvelope(preview), true);
});

test("projects exact DM World directory records into people, factions, and holdings", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    locationDirectory: [
      { id: "location.thalorien.brackenford", name: "Brackenford", kind: "settlement" },
      {
        id: "location.thalorien.aldros",
        name: "Aldros",
        kind: "region",
        summary: "Aldros is the central, only landlocked kingdom of Thalorien.",
      },
    ],
    worldDirectory: {
      people: [{
        id: "actor.thalorien.brackenford.elian-voss",
        name: "Elian Voss",
        kind: "NPC",
        locationId: "location.thalorien.brackenford",
        motive: { status: "active", visibility: "party", summary: "He watches the old road." },
      }],
      factions: [{
        id: "faction.thalorien.gilded-concord",
        name: "The Gilded Concord",
        status: "active",
        visibility: "gm",
        summary: "A disciplined merchant compact.",
        goals: ["Control the western trade."],
        methods: ["Contracts and pressure."],
        assets: ["Caravans"],
        agenda: { state: "ready", summary: "Secure the Brackenford road." },
        memberIds: ["actor.thalorien.brackenford.elian-voss"],
        territoryIds: ["location.thalorien.brackenford"],
        alliedIds: [],
        opposedIds: [],
      }],
      holdings: [{
        id: "chest.thalorien.brackenford.common-room",
        name: "Common-room chest",
        locationId: "location.thalorien.brackenford",
        kind: "chest",
      }],
    },
  }));

  assert.equal(envelope.world.people[0].name, "Elian Voss");
  assert.equal(envelope.world.locations[0].people[0].motive, "He watches the old road.");
  assert.equal(envelope.world.locations[0].holdings[0].name, "Common-room chest");
  const concord = envelope.world.factions.find(({ id }) => id === "faction.thalorien.gilded-concord");
  const aldros = envelope.world.factions.find(({ id }) => id === "location.thalorien.aldros");
  assert.equal(concord.name, "The Gilded Concord");
  assert.equal(concord.kind, "Organization");
  assert.deepEqual(concord.assets, ["Caravans"]);
  assert.deepEqual(concord.territories.map(({ id }) => id), [
    "location.thalorien.brackenford",
  ]);
  assert.equal(aldros.kind, "Sovereign power");
  assert.equal(aldros.influence, "Kingdom");
  assert.deepEqual(aldros.territories.map(({ id }) => id), ["location.thalorien.aldros"]);
});

test("DM Player-preview ignores trusted-GM location and World directories", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "player", allowedPerspectives: ["dm", "player"] },
    locations: [{
      name: "Brackenford",
      entries: [{ text: "Player-known village.", stance: "known", presentationKind: "statement" }],
    }],
    locationDirectory: [{ id: "location.thalorien.harrowfall", name: "Harrowfall" }],
    worldDirectory: {
      people: [{ id: "actor.secret", name: "Secret actor", kind: "NPC", locationId: "location.thalorien.harrowfall" }],
      factions: [],
      holdings: [],
    },
  }));

  assert.deepEqual(envelope.world.locations.map(({ name }) => name), ["Brackenford"]);
  assert.deepEqual(envelope.world.people, []);
  assert.deepEqual(envelope.world.factions, []);
  assert.equal(JSON.stringify(envelope).includes("Secret actor"), false);
  assert.equal(JSON.stringify(envelope).includes("Harrowfall"), false);
});

test("classifies Thalorien turning points as history and enduring information as lore", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    knowledgeEntries: [
      {
        text: "The Great Seven-Kingdom War\nThe seven kingdoms fought a devastating war. Its settlement still constrains the rulers.\nThalorien",
        stance: "known",
        presentationKind: "statement",
      },
      {
        text: "The Hearthside Custom\nTravelers are welcomed with a place beside the hearth.\nThalorien",
        stance: "known",
        presentationKind: "statement",
      },
    ],
  }));

  assert.equal(envelope.world.history.length, 1);
  assert.equal(envelope.world.history[0].title, "The Great Seven-Kingdom War");
  assert.equal(envelope.world.history[0].era, "The Great Thalos War");
  assert.equal(envelope.world.lore.length, 1);
  assert.equal(envelope.world.lore[0].title, "The Hearthside Custom");
  assert.equal(envelope.world.lore[0].category, "World lore");
  assert.equal(envelope.world.history.length + envelope.world.lore.length, 2);
});

test("normalizes damaged dash characters in reviewed history titles", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    knowledgeEntries: [{
      text: "The Merceros�Valeros War Alliance\nMerceros and Valeros formed a wartime alliance.\nThalorien",
      stance: "known",
      presentationKind: "statement",
    }],
  }));

  assert.equal(envelope.world.history.length, 1);
  assert.equal(envelope.world.history[0].title, "The Merceros�Valeros War Alliance");
  assert.equal(envelope.world.lore.length, 0);
});

test("keeps unstructured authorized text intact as generic lore", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    knowledgeEntries: [{
      text: "A fragment without a record heading.",
      stance: "familiar",
      presentationKind: "recognition",
    }],
  }));

  assert.equal(envelope.world.history.length, 0);
  assert.equal(envelope.world.lore.length, 1);
  assert.equal(envelope.world.lore[0].title, "Known information 1");
  assert.equal(envelope.world.lore[0].body, "A fragment without a record heading.");
});

test("projects connected server knowledge locations into world locations", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    locations: [{
      name: "Brackenford",
      entries: [{ text: "Known place note.", stance: "known", presentationKind: "statement" }],
    }],
  }));

  assert.equal(envelope.world.locations.length, 1);
  assert.equal(envelope.world.locations[0].id, "live-location-1");
  assert.equal(envelope.world.currentLocationId, "");
  assert.equal(envelope.world.maps[0].features.length, 0);
  assert.equal(envelope.world.locations[0].playerKnown, true);
  assert.equal(isReadyHubEnvelope(envelope), true);
  assert.equal(envelope.world.regions.length, 1);
  assert.equal(envelope.world.regions[0].name, "Live location");
  assert.equal(envelope.world.regions[0].count, 1);
});

test("keeps the world identity separate from the selected campaign title", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture());

  assert.equal(envelope.world.name, "Thalorien");
  assert.equal(envelope.campaign.title, "The Waystone at Brackenford");
});

test("projects live chapter and arc continuity into the existing Campaign pages", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    chapters: [
      {
        id: "campaign.thalorien.brackenford.chapter.arrivals",
        status: "active",
        title: "Brackenford Arrivals",
        partyQuestion: "Why have the goblins stopped raiding outward?",
        createdAtUtc: "2026-08-22T21:04:50Z",
        updatedAtUtc: "2026-08-23T10:00:00Z",
      },
      {
        id: "campaign.thalorien.brackenford.chapter.first-night",
        status: "closed",
        title: "The First Night",
        partyQuestion: "What changed after the first night?",
        closingSummary: "The party earned the village's trust.",
        createdAtUtc: "2026-08-20T20:00:00Z",
        updatedAtUtc: "2026-08-21T20:00:00Z",
      },
    ],
    arcs: [
      {
        id: "campaign.thalorien.brackenford.arc.waking-depths",
        status: "active",
        title: "The Waking Depths",
        partyStake: "Brackenford's peace depends on finding the truth.",
        createdAtUtc: "2026-08-22T21:05:00Z",
        updatedAtUtc: "2026-08-23T10:01:00Z",
      },
      {
        id: "campaign.thalorien.brackenford.arc.old-road",
        status: "resolved",
        title: "The Old Road",
        partyStake: "Travel to Brackenford had become unsafe.",
        closingSummary: "The patrol road is open again.",
        createdAtUtc: "2026-08-18T10:00:00Z",
        updatedAtUtc: "2026-08-19T10:00:00Z",
      },
    ],
  }));

  assert.equal(envelope.campaign.chapter, "Brackenford Arrivals");
  assert.equal(envelope.campaign.question, "Why have the goblins stopped raiding outward?");
  assert.equal(envelope.campaign.stakes, "Brackenford's peace depends on finding the truth.");
  assert.equal(envelope.campaign.adventureLog[0].title, "The First Night");
  assert.equal(envelope.campaign.adventureLog[0].result, "The party earned the village's trust.");
  assert.equal(envelope.campaign.outcomes[0].title, "The Old Road");
  assert.equal(envelope.campaign.outcomes[0].result, "The patrol road is open again.");
  assert.deepEqual(envelope.campaign.threads.map((thread) => thread.title), [
    "Brackenford Arrivals",
    "The Waking Depths",
  ]);
  assert.equal(envelope.campaign.clues.length, 0);
  assert.equal(envelope.campaign.placesVisited.length, 0);
});

test("projects ended session recaps and authorized evidence into Campaign records", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    knowledgeEntries: [
      { text: "Waystone shard\nThe shard hums beside old boundary stones.", stance: "suspected", presentationKind: "evidence" },
      { text: "Ordinary setting lore.", stance: "known", presentationKind: "statement" },
    ],
    sessions: [{
      id: "session.thalorien.brackenford.1",
      status: "ended",
      ordinal: 1,
      updatedAtUtc: "2026-08-25T20:00:00Z",
      recap: {
        chapter: {
          id: "campaign.thalorien.brackenford.chapter.arrivals",
          status: "active",
          title: "Brackenford Arrivals",
          partyQuestion: "Why have the goblins stopped raiding?",
        },
        arc: {
          id: "campaign.thalorien.brackenford.arc.waking-depths",
          status: "active",
          title: "The Waking Depths",
          partyStake: "Brackenford's peace depends on finding the truth.",
        },
        milestones: [{
          chapterId: "campaign.thalorien.brackenford.chapter.first-night",
          title: "The First Night",
          closingSummary: "The party earned the village's trust.",
          timestamp: "2026-08-25T19:30:00Z",
          sequence: 0,
        }],
      },
    }],
  }));

  assert.equal(envelope.campaign.adventureLog[0].session, "Session 1");
  assert.equal(envelope.campaign.adventureLog[0].result, "The party earned the village's trust.");
  assert.equal(envelope.campaign.clues[0].title, "Waystone shard");
  assert.equal(envelope.campaign.clues[0].status, "Suspected");
  assert.equal(envelope.world.lore.some((entry) => entry.title === "Waystone shard"), false);
});

test("uses campaign location directory when a DM is in DM perspective", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    locations: [{
      name: "Brackenford",
      entries: [{ text: "Known place note.", stance: "known", presentationKind: "statement" }],
    }],
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    locationDirectory: [
      { id: "location.thalorien.brackenford", name: "Brackenford" },
      { id: "location.thalorien.crownmere", name: "Crownmere" },
      { id: "location.thalorien.southwestern-volcanic-region", name: "Southwestern Volcanic Region" },
    ],
  }));

  assert.equal(envelope.world.locations.length, 3);
  assert.equal(envelope.world.currentLocationId, "");
  assert.equal(envelope.world.maps[0].features.length, 0);
  assert.equal(envelope.world.regions.length, 3);
  const byName = Object.fromEntries(envelope.world.regions.map((region) => [region.name, region.count]));
  assert.equal(byName["Brackenford"], 1);
  assert.equal(byName["Crownmere"], 1);
  assert.equal(byName["Southwestern Volcanic Region"], 1);
});

test("infers region names for DM directory locations from known region summaries", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    locationDirectory: [
      { id: "location.thalorien.brackenford", name: "Brackenford", summary: "A cozy Valeros frontier village on the settled edge of a smaller, ancient forest." },
      { id: "location.thalorien.crownmere", name: "Crownmere", summary: "The courtly capital of Aldros and seat of diplomacy for the central kingdoms." },
      { id: "location.thalorien.southwestern-volcano", name: "Southwestern Volcano", summary: "The great volcano of Waylos's Southwestern Volcanic Region." },
      { id: "location.thalorien.aldros", name: "Aldros", kind: "Region", summary: "Aldros is the central kingdom of Thalorien." },
      { id: "location.thalorien.valeros", name: "Valeros", kind: "Region", summary: "Valeros is the southeastern kingdom of Thalorien." },
      { id: "location.thalorien.waylos", name: "Waylos", kind: "Region", summary: "Waylos is the southwestern kingdom of Thalorien." },
      { id: "location.thalorien.southwestern-volcanic-region", name: "Southwestern Volcanic Region", kind: "Region", summary: "A southwestern region of the continent." },
      { id: "location.thalorien.greenmantle", name: "The Greenmantle", summary: "A fortified estate." },
    ],
  }));

  const locationsById = Object.fromEntries(envelope.world.locations.map((location) => [location.id, location.region]));
  assert.equal(locationsById["location.thalorien.brackenford"], "Valeros");
  assert.equal(locationsById["location.thalorien.crownmere"], "Aldros");
  assert.equal(locationsById["location.thalorien.southwestern-volcano"], "Southwestern Volcanic Region");
  assert.equal(locationsById["location.thalorien.aldros"], "Aldros");
  assert.equal(
    envelope.world.regions.find((region) => region.name === "Valeros")?.count,
    2,
  );
  assert.equal(
    envelope.world.regions.find((region) => region.name === "Aldros")?.count,
    2,
  );
  assert.equal(
    envelope.world.regions.find((region) => region.name === "Southwestern Volcanic Region")?.count,
    2,
  );
});

test("infers region names from location container hierarchy", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    locationDirectory: [
      { id: "location.thalorien.aldros", name: "Aldros", kind: "Region", summary: "Aldros is the central kingdom." },
      { id: "location.thalorien.valeros", name: "Valeros", kind: "Region", summary: "Valeros is a southern kingdom." },
      { id: "location.thalorien.waylos", name: "Waylos", kind: "Region", summary: "Waylos is the southwestern kingdom." },
      { id: "location.thalorien.southwestern-volcanic-region", name: "Southwestern Volcanic Region", kind: "Region" },
      {
        id: "location.thalorien.greenmantle",
        name: "The Greenmantle",
        containerId: "location.thalorien.valeros",
        summary: "A fortified estate.",
      },
      {
        id: "location.thalorien.emberwright-tower",
        name: "The Emberwright Tower",
        containerId: "location.thalorien.waylos",
        summary: "A tall watchtower.",
      },
      {
        id: "location.thalorien.brackenford",
        name: "Brackenford",
        containerId: "location.thalorien.valeros",
        summary: "A cozy village by the old road.",
      },
      {
        id: "location.thalorien.southwestern-volcano",
        name: "Southwestern Volcano",
        containerId: "location.thalorien.southwestern-volcanic-region",
        summary: "A volcano.",
      },
    ],
  }));

  const locationsById = Object.fromEntries(envelope.world.locations.map((location) => [location.id, location.region]));
  assert.equal(locationsById["location.thalorien.greenmantle"], "Valeros");
  assert.equal(locationsById["location.thalorien.emberwright-tower"], "Waylos");
  assert.equal(locationsById["location.thalorien.brackenford"], "Valeros");
  assert.equal(locationsById["location.thalorien.southwestern-volcano"], "Southwestern Volcanic Region");
});

test("uses exact live containment for cropped Region map membership", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    locationDirectory: [
      {
        id: "location.thalorien.thalos", name: "Thalos", kind: "region",
        containerId: "world.thalorien",
        mapVisual: { assetKey: "thalos.dm", alt: "DM Thalos" },
      },
      {
        id: "location.thalorien.aldros", name: "Aldros", kind: "region",
        containerId: "location.thalorien.thalos", mapAnchor: { x: 500, y: 407 },
        mapVisual: { assetKey: "thalos.region.aldros.dm", alt: "DM Aldros" },
      },
      {
        id: "location.thalorien.world-tree-grounds", name: "World Tree Grounds", kind: "region",
        containerId: "location.thalorien.thalos", mapAnchor: { x: 500, y: 559 },
      },
      {
        id: "location.thalorien.world-tree",
        name: "The World Tree",
        kind: "site",
        containerId: "location.thalorien.aldros",
        mapAnchor: { x: 500, y: 342 },
      },
    ],
  }));

  const aldros = envelope.world.maps.find((map) => map.subject.id === "location.thalorien.aldros");
  const grounds = envelope.world.maps.find(
    (map) => map.subject.id === "location.thalorien.world-tree-grounds",
  );
  assert.deepEqual(aldros?.features.map((feature) => feature.name), ["The World Tree"]);
  assert.equal(grounds, undefined);
  assert.equal(isReadyHubEnvelope(envelope), true);
});

test("groups live map markers into deterministic location-kind layers", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    locationDirectory: [
      {
        id: "location.thalorien.thalos", name: "Thalos", kind: "region",
        containerId: "world.thalorien", mapVisual: { assetKey: "thalos.dm", alt: "DM Thalos" },
      },
      {
        id: "location.thalorien.aldros", name: "Aldros", kind: "region",
        containerId: "location.thalorien.thalos", mapAnchor: { x: 100, y: 100 },
      },
      {
        id: "location.thalorien.crownmere",
        name: "Crownmere",
        kind: "settlement",
        containerId: "location.thalorien.thalos",
        mapAnchor: { x: 200, y: 200 },
      },
      {
        id: "location.thalorien.larkspire-university",
        name: "Larkspire University",
        kind: "site",
        containerId: "location.thalorien.thalos",
        mapAnchor: { x: 300, y: 300 },
      },
      {
        id: "location.thalorien.world-tree", name: "The World Tree", kind: "interior",
        containerId: "location.thalorien.thalos", mapAnchor: { x: 400, y: 400 },
      },
      {
        id: "location.thalorien.world-tree-grounds", name: "World Tree Grounds",
        containerId: "location.thalorien.thalos", mapAnchor: { x: 500, y: 500 },
      },
    ],
  }));

  const worldMap = envelope.world.maps[0];
  assert.deepEqual(worldMap.layers.map(({ id, label }) => ({ id, label })), [
    { id: "layer.live.world.regions", label: "Regions" },
    { id: "layer.live.world.settlements", label: "Settlements" },
    { id: "layer.live.world.sites", label: "Sites & interiors" },
    { id: "layer.live.world.other", label: "Other places" },
  ]);
  assert.deepEqual(
    worldMap.features.map((feature) => feature.layerId),
    [
      "layer.live.world.regions",
      "layer.live.world.settlements",
      "layer.live.world.sites",
      "layer.live.world.sites",
      "layer.live.world.other",
    ],
  );
  assert.equal(isReadyHubEnvelope(envelope), true);
});

test("links illustrative Crownmere and Merrowgate city maps from their exact Regions", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    locationDirectory: [
      {
        id: "location.thalorien.thalos", name: "Thalos", kind: "region",
        containerId: "world.thalorien", mapVisual: { assetKey: "thalos.dm", alt: "DM Thalos" },
      },
      {
        id: "location.thalorien.aldros", name: "Aldros", kind: "region",
        containerId: "location.thalorien.thalos", mapAnchor: { x: 500, y: 407 },
        mapVisual: { assetKey: "thalos.region.aldros.dm", alt: "DM Aldros" },
      },
      {
        id: "location.thalorien.merceros", name: "Merceros", kind: "region",
        containerId: "location.thalorien.thalos", mapAnchor: { x: 500, y: 827 },
        mapVisual: { assetKey: "thalos.region.merceros.dm", alt: "DM Merceros" },
      },
      {
        id: "location.thalorien.crownmere",
        name: "Crownmere",
        kind: "settlement",
        containerId: "location.thalorien.aldros",
        mapAnchor: { x: 692, y: 516 },
        mapVisual: { assetKey: "thalos.city.crownmere.dm", alt: "DM Crownmere" },
      },
      {
        id: "location.thalorien.merrowgate",
        name: "Merrowgate",
        kind: "settlement",
        containerId: "location.thalorien.merceros",
        mapAnchor: { x: 515, y: 668 },
        mapVisual: { assetKey: "thalos.city.merrowgate.dm", alt: "DM Merrowgate" },
      },
    ],
  }));

  const crownmere = envelope.world.maps.find((map) => map.subject.id === "location.thalorien.crownmere");
  const merrowgate = envelope.world.maps.find((map) => map.subject.id === "location.thalorien.merrowgate");
  const aldros = envelope.world.maps.find((map) => map.subject.id === "location.thalorien.aldros");
  const merceros = envelope.world.maps.find((map) => map.subject.id === "location.thalorien.merceros");

  assert.equal(crownmere?.parentMapId, "map.live.location.thalorien.aldros");
  assert.equal(crownmere?.base.imageUrl, "/city-map-crownmere-v2.png");
  assert.deepEqual(crownmere?.features, []);
  assert.equal(merrowgate?.parentMapId, "map.live.location.thalorien.merceros");
  assert.equal(merrowgate?.base.imageUrl, "/city-map-merrowgate-v2.png");
  assert.deepEqual(merrowgate?.features, []);
  assert.deepEqual(
    aldros?.scopeLinks.map(({ childMapId, viaFeatureId }) => ({ childMapId, viaFeatureId })),
    [{
      childMapId: "map.live.location.thalorien.crownmere",
      viaFeatureId: "feature.live.location.thalorien.aldros.location.thalorien.crownmere",
    }],
  );
  assert.deepEqual(
    merceros?.scopeLinks.map(({ childMapId, viaFeatureId }) => ({ childMapId, viaFeatureId })),
    [{
      childMapId: "map.live.location.thalorien.merrowgate",
      viaFeatureId: "feature.live.location.thalorien.merceros.location.thalorien.merrowgate",
    }],
  );
  assert.equal(isReadyHubEnvelope(envelope), true);
});

test("omits a city scope when the settlement is unauthorized or has the wrong parent", () => {
  const actor = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "player", allowedPerspectives: ["player"] },
    locations: [{
      name: "Brackenford",
      entries: [{ text: "Known place note.", stance: "known", presentationKind: "statement" }],
    }],
  }));
  const wrongParent = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    locationDirectory: [
      { id: "location.thalorien.valeros", name: "Valeros", kind: "region" },
      {
        id: "location.thalorien.crownmere",
        name: "Crownmere",
        kind: "settlement",
        containerId: "location.thalorien.valeros",
      },
    ],
  }));

  assert.equal(JSON.stringify(actor).includes("city-map-crownmere"), false);
  assert.equal(JSON.stringify(actor).includes("city-map-merrowgate"), false);
  assert.equal(wrongParent.world.maps.some((map) => map.scope === "city"), false);
  assert.equal(isReadyHubEnvelope(actor), true);
});

test("projects exact actor-authorized location knowledge onto every visible map scope", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "player", allowedPerspectives: ["player"] },
    locationDirectoryAudience: "player",
    locationDirectory: [
      {
        id: "location.thalorien.thalos", name: "Thalos", kind: "region",
        containerId: "world.thalorien", mapVisual: { assetKey: "thalos.player", alt: "Player Thalos" },
      },
      {
        id: "location.thalorien.valeros", name: "Valeros", kind: "region",
        containerId: "location.thalorien.thalos", mapAnchor: { x: 700, y: 667 },
        mapVisual: { assetKey: "thalos.region.valeros.player", alt: "Player Valeros" },
      },
      {
        id: "location.thalorien.brackenford", name: "Brackenford", kind: "settlement",
        containerId: "location.thalorien.valeros", mapAnchor: { x: 232, y: 647 },
      },
    ],
    locations: [{
      name: "Brackenford",
      entries: [
        { text: "The old well is safe.", stance: "known", presentationKind: "statement" },
        { text: "The west road floods after rain.", stance: "known", presentationKind: "statement" },
      ],
    }],
  }));

  assert.equal(envelope.campaign.mapOverlays.length, 1);
  assert.deepEqual(
    envelope.campaign.mapOverlays.map(({ mapId, label }) => ({ mapId, label })),
    [{ mapId: "map.live.location.thalorien.valeros", label: "Brackenford knowledge" }],
  );
  assert.match(envelope.campaign.mapOverlays[0].detail, /old well.*west road/iu);
  for (const overlay of envelope.campaign.mapOverlays) {
    assert.equal("geometry" in overlay, false);
    assert.equal("layerId" in overlay, false);
  }
  assert.equal(isReadyHubEnvelope(envelope), true);
});

test("projects GM-authorized notes in DM perspective but fails closed in local Player preview", () => {
  const input = {
    locations: [{
      name: "Crownmere",
      entries: [{ text: "The court record is disputed.", stance: "known", presentationKind: "statement" }],
    }],
    locationDirectory: [
      {
        id: "location.thalorien.thalos", name: "Thalos", kind: "region",
        containerId: "world.thalorien", mapVisual: { assetKey: "thalos.dm", alt: "DM Thalos" },
      },
      {
        id: "location.thalorien.aldros", name: "Aldros", kind: "region",
        containerId: "location.thalorien.thalos", mapAnchor: { x: 500, y: 407 },
        mapVisual: { assetKey: "thalos.region.aldros.dm", alt: "DM Aldros" },
      },
      {
        id: "location.thalorien.crownmere", name: "Crownmere", kind: "settlement",
        containerId: "location.thalorien.aldros", mapAnchor: { x: 692, y: 516 },
      },
    ],
  };
  const dm = connectedCampaignToHubEnvelope(connectedFixture({
    ...input,
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
  }));
  const preview = connectedCampaignToHubEnvelope(connectedFixture({
    ...input,
    audience: { seat: "dm", perspective: "player", allowedPerspectives: ["dm", "player"] },
  }));

  assert.equal(dm.campaign.mapOverlays.length, 1);
  assert.deepEqual(preview.campaign.mapOverlays, []);
  assert.equal(isReadyHubEnvelope(dm), true);
  assert.equal(isReadyHubEnvelope(preview), true);
});

test("drops unmatched live knowledge without leaving text, names, counts, or placeholders", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "player", allowedPerspectives: ["player"] },
    locations: [{
      name: "Unplaced Secret Annex",
      entries: [{ text: "CANARY-UNPLACED-NOTE", stance: "known", presentationKind: "statement" }],
    }],
  }));

  assert.deepEqual(envelope.campaign.mapOverlays, []);
  assert.equal(JSON.stringify(envelope.campaign.mapOverlays).includes("CANARY-UNPLACED-NOTE"), false);
  assert.equal(isReadyHubEnvelope(envelope), true);
});

test("uses only authorized knowledge when a DM asks for player perspective", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    locations: [{
      name: "Brackenford",
      entries: [{ text: "Known place note.", stance: "known", presentationKind: "statement" }],
    }],
    audience: { seat: "dm", perspective: "player", allowedPerspectives: ["dm", "player"] },
    locationDirectory: [
      { id: "location.thalorien.brackenford", name: "Brackenford" },
      { id: "location.thalorien.crownmere", name: "Crownmere" },
    ],
  }));

  assert.equal(envelope.world.locations.length, 1);
  assert.equal(envelope.world.currentLocationId, "");
  assert.equal(envelope.world.maps[0].features.length, 0);
  assert.equal(JSON.stringify(envelope).includes("Crownmere"), false);
});

test("projects country-like location names into regions", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    locations: [
      {
        name: "Brackenford, Eldervale",
        entries: [{ text: "A note from Eldervale.", stance: "known", presentationKind: "statement" }],
      },
      {
        name: "Hollow Beacon, Eldervale",
        entries: [{ text: "Another Eldervale note.", stance: "known", presentationKind: "statement" }],
      },
      {
        name: "New Capital City - Crown Coast",
        entries: [{ text: "A note from the coast.", stance: "known", presentationKind: "statement" }],
      },
    ],
  }));

  assert.equal(envelope.world.regions.length, 2);
  const byName = Object.fromEntries(envelope.world.regions.map((region) => [region.name, region.count]));
  assert.equal(byName["Eldervale"], 2);
  assert.equal(byName["Crown Coast"], 1);
  assert.equal(envelope.world.locations[0].region, "Eldervale");
  assert.equal(envelope.world.locations[1].region, "Eldervale");
  assert.equal(envelope.world.locations[2].region, "Crown Coast");
});

test("falls back to an unprojected placeholder when no known locations are available", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    knowledgeStatus: "unavailable",
    locations: [],
  }));

  assert.equal(envelope.world.locations.length, 1);
  assert.equal(envelope.world.currentLocationId, "");
  assert.equal(envelope.world.locations[0].name, "Current location not recorded");
  assert.equal(envelope.world.maps[0].features.length, 0);
  assert.equal(isReadyHubEnvelope(envelope), true);
});

test("uses only an exact server-projected current location admitted by the location directory", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    currentLocationId: "location.thalorien.brackenford",
    audience: { seat: "player", perspective: "player", allowedPerspectives: ["player"] },
    locationDirectoryAudience: "player",
    locationDirectory: [{
      id: "location.thalorien.brackenford",
      name: "Brackenford",
      kind: "settlement",
      summary: "A frontier village.",
    }],
  }));

  assert.equal(envelope.world.currentLocationId, "location.thalorien.brackenford");
  assert.equal(isReadyHubEnvelope(envelope), true);
});
