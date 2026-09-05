import assert from "node:assert/strict";
import test from "node:test";

import { connectedCampaignToHubEnvelope } from "../src/server/connected-hub-envelope.ts";
import { isReadyHubEnvelope } from "../src/state.js";

function visual(id, alt) {
  return {
    imageUrl: `/api/applications/dnd2024/state-spaces/dnd2024-main/entities/${id}/media/map/content`,
    alt,
  };
}

function connectedFixture({
  knowledgeStatus = "ready",
  knowledgeEntries = [{ text: "A placeholder campaign fact.", stance: "known", presentationKind: "statement" }],
  chronologyStatus = "empty",
  chronologyEntries = [],
  locations = [],
  audience,
  locationDirectoryAudience,
  locationDirectory,
  worldDirectory,
  chapters = [],
  arcs = [],
  sessions = [],
  visits = [],
  currentLocationId,
  currentSituation,
  knownRoutes,
  chronology,
  rules,
  party,
} = {}) {
  return {
    version: 1,
    status: "connected",
    applicationId: "dnd2024",
    stateSpaceId: "dnd2024-main",
    ...(currentLocationId ? { currentLocationId } : {}),
    ...(currentSituation ? { currentSituation } : {}),
    ...(knownRoutes ? { knownRoutes } : {}),
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
      visits,
    },
    actor: { id: "orban", name: "Orban", state: null, entries: [] },
    ...(party ? { party } : {}),
    knowledge: {
      status: knowledgeStatus,
      entries: knowledgeEntries,
      locations,
    },
    chronology: chronology ?? {
      status: chronologyStatus,
      perspective: audience?.perspective ?? audience?.seat ?? "player",
      entries: chronologyEntries,
    },
    ...(locationDirectoryAudience ? { locationDirectoryAudience } : {}),
    ...(locationDirectory ? { locationDirectory } : {}),
    ...(worldDirectory ? { worldDirectory } : {}),
    ...(rules ? { rules } : {}),
  };
}

test("projects authorized entity media into locations, people, clues, and the exact current conversation", () => {
  const portrait = { imageUrl: "/assets/tibb.png", alt: "Tibb Fallow", width: 1024, height: 1536 };
  const setting = { imageUrl: "/assets/bramblebridge.png", alt: "Bramblebridge market", width: 1536, height: 1024 };
  const handout = { imageUrl: "/assets/token.png", alt: "Thirteen-stroke token", width: 1254, height: 1254 };
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    currentLocationId: "location.caldris.bramblebridge",
    currentSituation: {
      status: "ready",
      kind: "conversation",
      locationId: "location.caldris.bramblebridge",
      scene: setting,
      conversation: {
        id: "interaction.caldris.tibb",
        name: "A word with Tibb",
        participants: [{ id: "actor.caldris.tibb-fallow", name: "Tibb Fallow", portrait }],
      },
    },
    locationDirectoryAudience: "dm",
    locationDirectory: [{
      id: "location.caldris.bramblebridge",
      name: "Bramblebridge",
      kind: "settlement",
      summary: "A bridge-market town.",
      media: { setting },
    }],
    worldDirectory: {
      people: [{
        id: "actor.caldris.tibb-fallow",
        name: "Tibb Fallow",
        kind: "NPC",
        locationId: "location.caldris.bramblebridge",
        media: { portrait },
      }],
      factions: [],
      holdings: [],
    },
    knowledgeEntries: [{
      text: "The River's Thirteenth Stroke\nThe timing matches a quay signal.",
      stance: "known",
      presentationKind: "evidence",
      subject: { id: "clue.caldris.q01.barge-timing", name: "The River's Thirteenth Stroke" },
      media: { handout },
    }],
  }));

  assert.equal(envelope.applicationId, "dnd2024");
  assert.equal(envelope.stateSpaceId, "dnd2024-main");
  assert.deepEqual(envelope.world.locations[0].media?.setting, setting);
  assert.deepEqual(envelope.world.people[0].portrait, portrait);
  assert.deepEqual(envelope.world.locations[0].people[0].portrait, portrait);
  assert.deepEqual(envelope.campaign.clues[0].handout, handout);
  assert.deepEqual(envelope.currentSituation?.status === "ready" ? envelope.currentSituation.scene : null, setting);
  assert.deepEqual(
    envelope.currentSituation?.status === "ready" && envelope.currentSituation.kind === "conversation"
      ? envelope.currentSituation.conversation.participants[0].portrait
      : null,
    portrait,
  );
});

test("projects authorized location imagery into its parent map marker preview", () => {
  const setting = {
    imageUrl: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/location.child/media/visual-0/content",
    alt: "A market beside an old bridge",
    width: 1536,
    height: 1024,
  };
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "player", perspective: "player", allowedPerspectives: ["player"] },
    locationDirectoryAudience: "player",
    locationDirectory: [
      {
        id: "location.root", name: "Known World", kind: "region", containerId: "world.root",
        mapVisual: visual("location.root", "Known world map"),
      },
      {
        id: "location.child", name: "Bridge Market", kind: "settlement",
        containerId: "location.root", mapAnchor: { x: 420, y: 310 },
        summary: "A busy market beside the old bridge.", media: { setting },
      },
    ],
  }));

  const root = envelope.world.maps.find((map) => map.subject.id === "location.root");
  assert.deepEqual(root?.features[0].preview, setting);
  assert.equal(isReadyHubEnvelope(envelope), true);
});

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

test("prefers canonical character state and direct inventory over provisional notes", () => {
  const portrait = {
    imageUrl: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/actor.thalorien.brackenford.orban/media/visual-0/content",
    alt: "Orban portrait",
    width: 800,
    height: 1200,
  };
  const itemIllustration = {
    imageUrl: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/item.orban.gold/media/visual-0/content",
    alt: "A pouch of gold pieces",
    width: 800,
    height: 800,
  };
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "player", allowedPerspectives: ["player"] },
    party: [{
      id: "actor.thalorien.brackenford.orban",
      name: "Orban",
      state: "active",
      current: true,
      media: { portrait },
      entries: [
        { kind: "class", key: "bard", label: "Provisional Bard direction" },
        { kind: "equipment", key: "ocarina", label: "Narrative ocarina" },
      ],
      canonical: {
        version: 2,
        subject: { id: "actor.thalorien.brackenford.orban", label: "Orban" },
        identity: { appearance: "Tall and slender.", biography: "Raised by performers." },
        origin: {
          species: { id: "dnd2024.content.species.human", label: "Human" },
          background: { id: "dnd2024.content.background.criminal", label: "Criminal" },
        },
        abilities: [{ ability: { id: "dnd2024.vocabulary.ability.charisma", label: "Charisma" }, score: 17, modifier: 3 }],
        hitPoints: { current: 9, maximum: 9, maximumReduction: 0 },
        body: { size: { id: "dnd2024.vocabulary.size.medium", label: "Medium" } },
        movement: [{
          kind: { id: "dnd2024.vocabulary.movement-mode.walk", label: "Walk" },
          numerator: 9,
          denominator: 1,
          unit: { id: "dnd2024.vocabulary.distance-unit.meter", label: "Meter" },
        }],
        proficiencies: [{
          proficiency: { id: "dnd2024.vocabulary.skill.performance", label: "Performance" },
          rank: { id: "dnd2024.vocabulary.proficiency-rank.proficiency", label: "Proficiency" },
        }],
        experience: { total: 0 },
        classes: [{
          id: "actor.thalorien.brackenford.orban.class-membership.bard",
          name: "Bard membership",
          class: { id: "dnd2024.content.class.bard", label: "Bard" },
          level: 1,
          subclass: null,
        }],
        inventory: {
          contentsDepth: 4,
          mayOmitDeeperContents: true,
          items: [{
            id: "item.orban.gold",
            name: "Starting Gold",
            definition: { id: "dnd2024.content.item.currency.gold-piece", label: "Gold Piece" },
            quantity: 45,
            slot: "inventory.currency",
            parentItemId: null,
            order: 0,
            depth: 1,
            childCount: 0,
            deeperContentsOmitted: false,
            equipmentSlots: [],
            media: { illustration: itemIllustration },
          }],
        },
        wallet: {
          coinCount: 45,
          copperValue: 4500,
          gpCount: 45,
          denominations: [{
            denomination: { id: "dnd2024.content.item.currency.gold-piece", label: "Gold Piece" },
            code: "gp",
            count: 45,
            copperValuePerCoin: 100,
            totalCopperValue: 4500,
          }],
        },
        dossier: {
          origin: {
            species: { id: "dnd2024.content.species.human", label: "Human", canonicalName: "Human", kind: "species", status: "active", summary: null, source: null },
            background: { id: "dnd2024.content.background.criminal", label: "Criminal", canonicalName: "Criminal", kind: "background", status: "active", summary: null, source: null },
            traits: [],
          },
          classes: [{
            id: "actor.thalorien.brackenford.orban.class-membership.bard",
            name: "Bard membership",
            definition: { id: "dnd2024.content.class.bard", label: "Bard", canonicalName: "Bard", kind: "class", status: "active", summary: null, source: null },
            level: 1,
            subclass: null,
          }],
          features: [],
          inventory: {
            definitions: [{ id: "dnd2024.content.item.currency.gold-piece", label: "Gold Piece", canonicalName: "Gold Piece", kind: "equipment", status: "identity-only", summary: null, source: null }],
            contentsDepth: 4,
            mayOmitDeeperContents: true,
          },
          levelOneRules: {
            test: "character-level-one-rules-project",
            subjectId: "actor.thalorien.brackenford.orban",
            armorClass: {}, attacks: [], senses: [], savingThrowCircumstances: [], spellAccess: {}, equipment: {}, entitlements: [],
          },
          definitions: [],
          provenance: {
            sheetQueryId: "dnd2024.query.character-sheet-v2",
            sheetProjectionId: "dnd2024.mechanic.character-sheet-v2.project",
            dossierProjectionId: "dnd2024.mechanic.character-dossier-v1.project",
            definitionCount: 0,
            inventoryDepth: 4,
            ruleTextPolicy: "canonical-only",
          },
        },
      },
    }],
  }));

  const member = envelope.party[0];
  assert.deepEqual(member.portrait, portrait);
  assert.equal(member.recordStatus, "Canonical character state");
  assert.equal(member.sheetStatus, "canonical");
  assert.equal(member.inventoryStatus, "canonical");
  assert.equal(member.sheetState.status, "ready");
  assert.equal(member.sheetState.source, "canonical");
  assert.equal(member.inventoryState.status, "ready");
  assert.deepEqual(member.sheet.slice(0, 3).map((entry) => entry.title), [
    "Bard · Level 1",
    "Hit Points",
    "Charisma",
  ]);
  assert.deepEqual(member.origin.map((entry) => entry.title), ["Human", "Criminal"]);
  assert.deepEqual(member.backstory.map((entry) => entry.title), ["Appearance", "Biography"]);
  assert.deepEqual(member.inventory.map((entry) => entry.title), ["Starting Gold"]);
  assert.deepEqual(member.inventory[0].media, itemIllustration);
  assert.equal(JSON.stringify(member).includes("Provisional Bard direction"), false);
  assert.equal(JSON.stringify(member).includes("Narrative ocarina"), false);
  assert.equal(JSON.stringify(member).includes("armor class"), false);
});

test("canonical failures remain errors and never fall back to provisional character values", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "player", allowedPerspectives: ["player"] },
    party: [{
      id: "actor.one",
      name: "One",
      state: "active",
      current: true,
      entries: [
        { kind: "class", key: "wizard", label: "Invented fallback wizard" },
        { kind: "equipment", key: "wand", label: "Invented fallback wand" },
      ],
      canonicalResult: {
        status: "error",
        data: null,
        failureCategory: "incompatible-data",
        diagnosticId: "projection-malformed-1",
      },
    }],
  }));

  const member = envelope.party[0];
  assert.equal(member.sheetState.status, "error");
  assert.equal(member.sheetState.failureCategory, "incompatible-data");
  assert.equal(member.inventoryState.status, "error");
  assert.equal(member.detail, "Character details temporarily unavailable");
  assert.deepEqual(member.sheet, []);
  assert.deepEqual(member.inventory, []);
  assert.equal(member.characterSheet, undefined);
  assert.equal(JSON.stringify(member).includes("Invented fallback"), false);
});

test("catalog HTTP failures retain their status as explicit error sections", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "player", allowedPerspectives: ["player"] },
    party: [{
      id: "actor.one",
      name: "One",
      state: "active",
      current: true,
      entries: [],
      canonicalResult: {
        status: "error",
        data: null,
        failureCategory: "http",
        diagnosticId: "projection-catalog-422",
        httpStatus: 422,
      },
    }],
  }));

  assert.deepEqual(envelope.party[0].sheetState, {
    status: "error",
    data: null,
    failureCategory: "http",
    diagnosticId: "projection-catalog-422",
    httpStatus: 422,
  });
  assert.deepEqual(envelope.party[0].inventoryState, envelope.party[0].sheetState);
});

test("authorization failures are represented as forbidden section states", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "player", allowedPerspectives: ["player"] },
    party: [{
      id: "actor.one",
      name: "One",
      state: "active",
      current: true,
      entries: [],
      canonicalResult: {
        status: "forbidden",
        data: null,
        failureCategory: "authorization",
        diagnosticId: "projection-forbidden-1",
        httpStatus: 403,
      },
    }],
  }));

  assert.equal(envelope.party[0].sheetState.status, "forbidden");
  assert.equal(envelope.party[0].inventoryState.status, "forbidden");
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
    id: "dnd2024.rule.shared.search",
    resolutionKey: "rule.shared.search",
    title: "Search",
    summary: "Resolve a search through the active mechanic.",
    order: 10,
    section: { id: "activity", label: "Activity", order: 10 },
    blocks: [{ kind: "paragraph", heading: null, body: "Choose where and how to search.", items: [] }],
    examples: [],
    relatedRuleIds: [],
    citations: [{ sourceId: "source.fixture", locator: "Fixture, page 1" }],
    authority: { mechanicIds: ["dnd2024.mechanic.search"], procedureIds: [] },
    visibility: "public",
    source: { ownerId: "base", label: "Core", classification: "core" },
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

test("projects dedicated chronology as history and keeps history-like knowledge as lore", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    chronologyStatus: "ready",
    chronologyEntries: [{
      id: "chronology-1",
      occurredAtMinute: -120,
      dateLabel: "Year 412",
      precision: "exact",
      title: "The Gate Dedication",
      summary: "The northern gate was dedicated after the long reconstruction.",
    }],
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
  assert.equal(envelope.world.history[0].title, "The Gate Dedication");
  assert.equal(envelope.world.history[0].date, "Year 412");
  assert.equal(envelope.world.history[0].consequence, undefined);
  assert.equal(envelope.world.lore.length, 2);
  assert.deepEqual(envelope.world.lore.map((entry) => entry.title).sort(), [
    "The Great Seven-Kingdom War",
    "The Hearthside Custom",
  ]);
});

test("does not turn reviewed history-like knowledge into chronology", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    knowledgeEntries: [{
      text: "The Merceros�Valeros War Alliance\nMerceros and Valeros formed a wartime alliance.\nThalorien",
      stance: "known",
      presentationKind: "statement",
    }],
  }));

  assert.equal(envelope.world.history.length, 0);
  assert.equal(envelope.world.lore.length, 1);
  assert.equal(envelope.world.lore[0].title, "The Merceros�Valeros War Alliance");
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

test("attaches exact known ways only to the exploration scene location", () => {
  const originId = "location.thalorien.brackenford";
  const destinationId = "location.thalorien.crownmere";
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    currentLocationId: originId,
    currentSituation: { status: "ready", kind: "exploration", locationId: originId },
    locationDirectoryAudience: "player",
    locationDirectory: [
      { id: originId, name: "Brackenford", kind: "settlement", summary: "A frontier village." },
      { id: destinationId, name: "Crownmere", kind: "settlement", summary: "A port town." },
    ],
    knownRoutes: [{
      id: "route.thalorien.brackenford-to-crownmere",
      originId,
      destinationId,
      destinationName: "Crownmere",
      detail: "The Crownmere road is open.",
      mode: "on-foot",
      durationMinutes: 45,
    }],
  }));

  assert.deepEqual(envelope.world.locations.find((location) => location.id === originId)?.routes, [{
    destination: "Crownmere",
    detail: "The Crownmere road is open. · On foot, 45 minutes.",
  }]);
  assert.deepEqual(envelope.world.locations.find((location) => location.id === destinationId)?.routes, []);
});

test("omits known ways when the current scene is not exploration", () => {
  const originId = "location.thalorien.brackenford";
  const destinationId = "location.thalorien.crownmere";
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    currentSituation: {
      status: "ready",
      kind: "conversation",
      locationId: originId,
      conversation: { id: "interaction.brackenford", name: "Parley", participants: [] },
    },
    locationDirectoryAudience: "player",
    locationDirectory: [
      { id: originId, name: "Brackenford" },
      { id: destinationId, name: "Crownmere" },
    ],
    knownRoutes: [{
      id: "route.thalorien.brackenford-to-crownmere",
      originId,
      destinationId,
      destinationName: "Crownmere",
      detail: "The Crownmere road is open.",
      mode: "on-foot",
      durationMinutes: 45,
    }],
  }));
  assert.equal(envelope.world.locations.every((location) => location.routes.length === 0), true);
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

test("projects authorized Caldris quest seeds into honest prepared campaign pursuits", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    knowledgeEntries: [
      {
        text: "Q01 — The Thirteenth Bell\nHook: An empty tax wagon arrives in heavy rain. Layers: prank → diversion. Objectives: 1. Account for the wagon. 2. Explain the bell. 3. Decide what to disclose. Routes: witnesses or records. Clues: dry flour. Failure forward: the carriers leave tracks. Aftermath: local trust grows.",
        stance: "known",
        presentationKind: "statement",
      },
      {
        text: "Q02 — Chickens of Commercial Intent\nHook: Poultry tolls stop the market. Objectives: 1. Keep the market open. 2. Compare the orders. 3. Secure fair relief.",
        stance: "known",
        presentationKind: "statement",
      },
    ],
    chapters: [{
      id: "campaign.caldris.measure-of-mercy.chapter.the-thirteenth-bell",
      status: "active",
      title: "The Thirteenth Bell",
      partyQuestion: "Why did the bell ring thirteen times?",
    }],
  }));

  assert.equal(envelope.campaign.quests.length, 2);
  assert.deepEqual(envelope.campaign.quests.map((quest) => quest.title), [
    "The Thirteenth Bell",
    "Chickens of Commercial Intent",
  ]);
  assert.equal(envelope.campaign.quests[0].status, "Active");
  assert.equal(envelope.campaign.quests[0].kind, "Opening adventure");
  assert.deepEqual(envelope.campaign.quests[0].objectives.map((objective) => objective.text), [
    "Account for the wagon.",
    "Explain the bell.",
    "Decide what to disclose.",
  ]);
  assert.equal(envelope.campaign.quests[1].status, "Prepared");
  assert.match(envelope.campaign.quests[0].dmContext, /Failure forward/u);
});

test("keeps party goals as the quest fallback and tolerates malformed seed prose", () => {
  const fallback = connectedCampaignToHubEnvelope(connectedFixture({
    knowledgeEntries: [{ text: "Ordinary lore.", stance: "known", presentationKind: "statement" }],
  }));
  assert.equal(fallback.campaign.quests[0].kind, "Party goal");

  const malformed = connectedCampaignToHubEnvelope(connectedFixture({
    knowledgeEntries: [{
      text: "Q09 — A Quiet Problem\nThe detailed packet has not been written yet.",
      stance: "known",
      presentationKind: "statement",
    }],
  }));
  assert.equal(malformed.campaign.quests[0].title, "A Quiet Problem");
  assert.equal(malformed.campaign.quests[0].status, "Prepared");
  assert.deepEqual(malformed.campaign.quests[0].objectives, []);
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

test("projects only exact visible World targets into record and clue links", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    locationDirectory: [{
      id: "location.thalorien.brackenford",
      name: "Brackenford",
      kind: "settlement",
    }],
    worldDirectory: {
      people: [{
        id: "actor.thalorien.brackenford.elian-voss",
        name: "Elian Voss",
        kind: "NPC",
        locationId: "location.thalorien.brackenford",
      }],
      factions: [{
        id: "faction.thalorien.gilded-concord",
        name: "The Gilded Concord",
        status: "active",
        visibility: "gm",
        summary: "A merchant compact.",
        goals: ["Control trade."],
        methods: ["Contracts."],
        assets: [],
        agenda: { state: "ready", summary: "Secure the road." },
        memberIds: [],
        territoryIds: [],
        alliedIds: [],
        opposedIds: [],
      }],
      holdings: [],
    },
    knowledgeEntries: [{
      text: "Waystone shard\nThe shard hums beside old boundary stones.",
      stance: "known",
      presentationKind: "evidence",
      subject: { id: "location.thalorien.brackenford", name: "Untrusted duplicate label" },
    }, {
      text: "Hidden subject\nThis target is not in the projected World.",
      stance: "known",
      presentationKind: "evidence",
      subject: { id: "actor.thalorien.secret", name: "Secret actor" },
    }],
    arcs: [{
      id: "campaign.thalorien.brackenford.arc.old-road",
      status: "resolved",
      title: "The Old Road",
      partyStake: "Travel had become unsafe.",
      closingSummary: "The patrol road is open again.",
      createdAtUtc: "2026-08-18T10:00:00Z",
      updatedAtUtc: "2026-08-19T10:00:00Z",
      worldEntityIds: [
        "location.thalorien.brackenford",
        "actor.thalorien.brackenford.elian-voss",
        "faction.thalorien.gilded-concord",
        "actor.thalorien.secret",
      ],
    }],
    sessions: [{
      id: "session.thalorien.brackenford.1",
      status: "ended",
      ordinal: 1,
      updatedAtUtc: "2026-08-25T20:00:00Z",
      worldEntityIds: ["location.thalorien.brackenford", "location.other-world.secret"],
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
        milestones: [],
      },
    }],
  }));

  assert.deepEqual(envelope.campaign.adventureLog[0].links, {
    locations: [{ id: "location.thalorien.brackenford", name: "Brackenford" }],
    people: [],
    factions: [],
  });
  assert.deepEqual(envelope.campaign.outcomes[0].links, {
    locations: [{ id: "location.thalorien.brackenford", name: "Brackenford" }],
    people: [{ id: "actor.thalorien.brackenford.elian-voss", name: "Elian Voss", kind: "NPC" }],
    factions: [{ id: "faction.thalorien.gilded-concord", name: "The Gilded Concord" }],
  });
  assert.deepEqual(envelope.campaign.clues[0].links.locations, [{
    id: "location.thalorien.brackenford",
    name: "Brackenford",
  }]);
  assert.deepEqual(envelope.campaign.clues[1].links, { locations: [], people: [], factions: [] });
  assert.equal(JSON.stringify(envelope.campaign).includes("Secret actor"), false);
  assert.equal(JSON.stringify(envelope.campaign).includes("other-world"), false);
});

test("projects explicit DM visit records and never infers visits for Player", () => {
  const visit = {
    id: "campaign-visit.thalorien.brackenford.village",
    locationId: "location.thalorien.brackenford",
    firstVisitedMinute: 120,
    lastVisitedMinute: 360,
    visitCount: 2,
    status: "departed",
    summary: "The frontier village beside the old road.",
    memory: "The party earned the village's trust.",
    gmContext: "The waystone is waking.",
  };
  const locationDirectory = [{
    id: "location.thalorien.brackenford",
    name: "Brackenford",
    kind: "settlement",
    summary: "A Valeros frontier village.",
  }];
  const dm = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    locationDirectory,
    visits: [visit, { ...visit, id: "campaign-visit.unknown", locationId: "location.other.secret" }],
    currentLocationId: "location.thalorien.brackenford",
  }));
  const preview = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "player", allowedPerspectives: ["dm", "player"] },
    locationDirectory,
    visits: [visit],
    currentLocationId: "location.thalorien.brackenford",
  }));

  assert.deepEqual(dm.campaign.placesVisited, [{
    id: "campaign-visit.thalorien.brackenford.village",
    location: {
      id: "location.thalorien.brackenford",
      name: "Brackenford",
      region: "Brackenford",
    },
    firstVisited: "Campaign minute 120",
    lastVisited: "Campaign minute 360",
    visitCount: 2,
    status: "Departed",
    summary: "The frontier village beside the old road.",
    memory: "The party earned the village's trust.",
    dmContext: "The waystone is waking.",
  }]);
  assert.deepEqual(preview.campaign.placesVisited, []);
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

test("a child with an unloaded parent image cannot become the world map", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    locationDirectory: [
      { id: "location.world", name: "World atlas", containerId: "world.thalorien",
        mapVisual: visual("world", "World atlas") },
      { id: "location.region", name: "Region", containerId: "location.world", mapAnchor: { x: 400, y: 400 } },
      { id: "location.child", name: "Aunholt", containerId: "location.region",
        mapVisual: visual("child", "Local map"), mapAnchor: { x: 500, y: 500 } },
    ],
  }));
  assert.equal(envelope.world.maps.find((map) => map.id === envelope.world.rootMapId).subject.name, "World atlas");
});

test("uses exact live containment for cropped Region map membership", () => {
  const envelope = connectedCampaignToHubEnvelope(connectedFixture({
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    locationDirectory: [
      {
        id: "location.thalorien.thalos", name: "Thalos", kind: "region",
        containerId: "world.thalorien",
        mapVisual: visual("thalos.dm", "DM Thalos"),
      },
      {
        id: "location.thalorien.aldros", name: "Aldros", kind: "region",
        containerId: "location.thalorien.thalos", mapAnchor: { x: 500, y: 407 },
        mapVisual: visual("thalos.region.aldros.dm", "DM Aldros"),
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
        containerId: "world.thalorien", mapVisual: visual("thalos.dm", "DM Thalos"),
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
        containerId: "world.thalorien", mapVisual: visual("thalos.dm", "DM Thalos"),
      },
      {
        id: "location.thalorien.aldros", name: "Aldros", kind: "region",
        containerId: "location.thalorien.thalos", mapAnchor: { x: 500, y: 407 },
        mapVisual: visual("thalos.region.aldros.dm", "DM Aldros"),
      },
      {
        id: "location.thalorien.merceros", name: "Merceros", kind: "region",
        containerId: "location.thalorien.thalos", mapAnchor: { x: 500, y: 827 },
        mapVisual: visual("thalos.region.merceros.dm", "DM Merceros"),
      },
      {
        id: "location.thalorien.crownmere",
        name: "Crownmere",
        kind: "settlement",
        containerId: "location.thalorien.aldros",
        mapAnchor: { x: 692, y: 516 },
        mapVisual: visual("thalos.city.crownmere.dm", "DM Crownmere"),
      },
      {
        id: "location.thalorien.merrowgate",
        name: "Merrowgate",
        kind: "settlement",
        containerId: "location.thalorien.merceros",
        mapAnchor: { x: 515, y: 668 },
        mapVisual: visual("thalos.city.merrowgate.dm", "DM Merrowgate"),
      },
    ],
  }));

  const crownmere = envelope.world.maps.find((map) => map.subject.id === "location.thalorien.crownmere");
  const merrowgate = envelope.world.maps.find((map) => map.subject.id === "location.thalorien.merrowgate");
  const aldros = envelope.world.maps.find((map) => map.subject.id === "location.thalorien.aldros");
  const merceros = envelope.world.maps.find((map) => map.subject.id === "location.thalorien.merceros");

  assert.equal(crownmere?.parentMapId, "map.live.location.thalorien.aldros");
  assert.equal(crownmere?.base.imageUrl, visual("thalos.city.crownmere.dm", "").imageUrl);
  assert.deepEqual(crownmere?.features, []);
  assert.equal(merrowgate?.parentMapId, "map.live.location.thalorien.merceros");
  assert.equal(merrowgate?.base.imageUrl, visual("thalos.city.merrowgate.dm", "").imageUrl);
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
        containerId: "world.thalorien", mapVisual: visual("thalos.player", "Player Thalos"),
      },
      {
        id: "location.thalorien.valeros", name: "Valeros", kind: "region",
        containerId: "location.thalorien.thalos", mapAnchor: { x: 700, y: 667 },
        mapVisual: visual("thalos.region.valeros.player", "Player Valeros"),
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
        containerId: "world.thalorien", mapVisual: visual("thalos.dm", "DM Thalos"),
      },
      {
        id: "location.thalorien.aldros", name: "Aldros", kind: "region",
        containerId: "location.thalorien.thalos", mapAnchor: { x: 500, y: 407 },
        mapVisual: visual("thalos.region.aldros.dm", "DM Aldros"),
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

test("preserves an exact adaptive current situation and rejects a dangling scene location", () => {
  const base = {
    currentLocationId: "location.thalorien.brackenford",
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
    locationDirectoryAudience: "dm",
    locationDirectory: [{
      id: "location.thalorien.brackenford",
      name: "Brackenford",
      kind: "settlement",
      summary: "A frontier village.",
    }],
  };
  const conversation = connectedCampaignToHubEnvelope(connectedFixture({
    ...base,
    currentSituation: {
      status: "ready",
      kind: "conversation",
      locationId: "location.thalorien.brackenford",
      conversation: {
        id: "interaction.brackenford.parley",
        name: "Gatehouse parley",
        summary: "The reeve asks what brought the party here.",
        participants: [{ id: "actor.reeve", name: "Reeve Mara" }],
      },
    },
  }));
  assert.equal(conversation.currentSituation.kind, "conversation");
  assert.equal(conversation.currentSituation.conversation.name, "Gatehouse parley");
  assert.equal(isReadyHubEnvelope(conversation), true);

  const dangling = connectedCampaignToHubEnvelope(connectedFixture({
    ...base,
    currentSituation: {
      status: "ready",
      kind: "exploration",
      locationId: "location.thalorien.hidden",
    },
  }));
  assert.deepEqual(dangling.currentSituation, {
    status: "unavailable",
    message: "The current scene location is unavailable.",
  });
});
