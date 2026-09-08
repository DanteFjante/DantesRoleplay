import assert from "node:assert/strict";
import test from "node:test";
import { boardEnvelope } from "./fixtures/encounter-board.js";
import { connectedCampaignToHubEnvelope } from "../src/server/connected-hub-envelope.ts";
import { resolveHubSurface } from "../src/data/hub-availability.js";
import { contract as campaignSummaryContract } from "../src/server/campaign-summary-contract.js";
import { contract as campaignContextContract } from "../src/server/campaign-context-contract.js";
import { contract as campaignDetailsContract } from "../src/server/campaign-details-contract.js";
import { contract as characterSheetContract } from "../src/server/character-sheet-contract.js";
import { contract as characterDossierContract } from "../src/server/character-dossier-contract.js";
import { contract as factionDirectoryContract } from "../src/server/faction-directory-contract.js";
import { contract as inventoryContainerContract } from "../src/server/inventory-container-contract.js";

import {
  inheritMediaVisual,
  normalizeGameServerOrigin,
  projectMediaVisual,
  readCombatCurrentScene,
  readCanonicalCharacter,
  readCanonicalCharacterSheet,
  readCanonicalInventory,
  readConversationCurrentScene,
  readGameServerContext,
  readRegisteredCampaignSummary,
  readRegisteredCampaignDetails,
  readRegisteredFactionDirectoryPage,
  readKnownOpenRoutes,
  resolveCurrentSceneRecord,
  resolveRecordedPlaySituation,
  resolveSceneAffordancesRecord,
  resolvePresenceLocation,
} from "../src/server/game-server-context.js";

function inventoryContainerData(actorId) {
  return {
    version: 1,
    owner: { id: actorId, label: "Ganji" },
    state: "ready",
    reasons: [],
    items: [
      {
        id: "inventory.backpack", name: "Backpack", definition: { id: "item.backpack", label: "Backpack" },
        quantity: 1, slot: "carried", parentItemId: null, order: 0, depth: 1, childCount: 1,
        deeperContentsOmitted: false, equipmentSlots: [], classification: "item",
      },
      {
        id: "inventory.rope", name: "Hempen rope", definition: { id: "item.rope", label: "Hempen rope" },
        quantity: 1, slot: "contained", parentItemId: "inventory.backpack", order: 0, depth: 2,
        childCount: 0, deeperContentsOmitted: false, equipmentSlots: [], classification: "item",
      },
    ],
    wallet: { coinCount: 3, copperValue: 300, gpCount: 3, denominations: [
      { denomination: { id: "currency.gp", label: "Gold piece" }, code: "gp", count: 3,
        copperValuePerCoin: 100, totalCopperValue: 300 },
    ] },
    limits: { contentsDepth: 4, itemCount: 100, complete: true },
  };
}

test("inventory container reads one bounded nested projection without item-tab fan-out", async () => {
  const calls = [];
  const actorId = "actor.ganji";
  const result = await readCanonicalInventory({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    actorId, perspective: "player",
    fetchImpl: async (input) => {
      calls.push(new URL(input));
      return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main", qualifiedQueryId: inventoryContainerContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: inventoryContainerContract.outputSchemaHash, resultFingerprint: "3".repeat(64),
        sourceRevisionFingerprint: "4".repeat(64), data: inventoryContainerData(actorId),
      });
    },
  });
  assert.equal(result.status, "ready");
  assert.deepEqual(result.data.items.map((item) => [item.id, item.parentItemId]), [
    ["inventory.backpack", null], ["inventory.rope", "inventory.backpack"],
  ]);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].searchParams.get("perspective"), "player");
  assert.match(calls[0].pathname, /dnd2024\.query\.inventory-container$/);
  assert.ok(calls.every((call) => !/recipes|uses|character-dossier/.test(call.pathname)));
});

test("inventory container rejects cycles and preserves authorization failures", async () => {
  const actorId = "actor.ganji";
  const cycle = inventoryContainerData(actorId);
  cycle.items[0].parentItemId = "inventory.rope";
  cycle.items[0].depth = 3;
  const incompatible = await readCanonicalInventory({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    actorId, perspective: "dm", fetchImpl: async () => response(200, {
      applicationId: "dnd2024", stateSpaceId: "dnd2024-main", qualifiedQueryId: inventoryContainerContract.id,
      stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
      outputSchemaHash: inventoryContainerContract.outputSchemaHash, resultFingerprint: "3".repeat(64),
      sourceRevisionFingerprint: "4".repeat(64), data: cycle,
    }),
  });
  assert.equal(incompatible.status, "error");
  assert.equal(incompatible.failureCategory, "incompatible-data");

  const forbidden = await readCanonicalInventory({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    actorId, perspective: "player", fetchImpl: async () => response(403, { code: "forbidden" }),
  });
  assert.equal(forbidden.status, "forbidden");
  assert.equal(forbidden.failureCategory, "authorization");
});

test("registered Campaign summary stays bounded and preserves read-only party references", async () => {
  const calls = [];
  const summary = await readRegisteredCampaignSummary({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    campaignId: "campaign.caldris.measure-of-mercy", perspective: "player",
    fetchImpl: async (input) => {
      calls.push(new URL(input));
      return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
        qualifiedQueryId: campaignSummaryContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: campaignSummaryContract.outputSchemaHash, resultFingerprint: "4".repeat(64),
        sourceRevisionFingerprint: "5".repeat(64), data: {
        status: "active", title: "The Measure of Mercy", premise: "Choose what mercy costs.",
        partyGoals: ["Protect Ganji."], toneAndBoundaries: ["No sexual violence."],
        party: [{ id: "participation.ganji", name: "Ganji participation", status: "active" }],
        totalCount: 1, complete: true, nextCursor: null,
      } });
    },
  });
  assert.equal(summary.title, "The Measure of Mercy");
  assert.equal(summary.projection.sourceRevisionFingerprint, "5".repeat(64));
  assert.equal(summary.projection.resolutionFingerprint, "2".repeat(64));
  assert.deepEqual(summary.party.map((entry) => entry.id), ["participation.ganji"]);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].searchParams.get("perspective"), "player");
  assert.ok(calls.every((call) => !call.pathname.includes("knowledge") && !call.pathname.includes("inventory")));
});

test("Player Campaign details reject GM fields and session records", async () => {
  const load = (data) => readRegisteredCampaignDetails({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    campaignId: "campaign.caldris.measure-of-mercy", perspective: "player",
    fetchImpl: async () => response(200, {
      applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
      qualifiedQueryId: campaignDetailsContract.id,
      stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
      outputSchemaHash: campaignDetailsContract.outputSchemaHash, resultFingerprint: "3".repeat(64),
      sourceRevisionFingerprint: "4".repeat(64), data,
    }),
  });
  const visible = await load({
    version: 1, campaignId: "campaign.caldris.measure-of-mercy",
    chapters: [{ id: "chapter.one", name: "One", status: "active", title: "One",
      partyQuestion: "What now?" }], arcs: [], sessions: [],
  });
  assert.equal(visible.chapters.length, 1);
  assert.equal(await load({
    version: 1, campaignId: "campaign.caldris.measure-of-mercy",
    chapters: [{ id: "chapter.one", name: "One", status: "active", title: "One",
      partyQuestion: "What now?", gmContext: "Secret" }], arcs: [], sessions: [],
  }), null);
  assert.equal(await load({
    version: 1, campaignId: "campaign.caldris.measure-of-mercy", chapters: [], arcs: [],
    sessions: [{ id: "session.one", name: "One", status: "active", ordinal: 1 }],
  }), null);
});

test("registered faction pages stay bounded and do not fan out into knowledge or inventory reads", async () => {
  const calls = [];
  const page = await readRegisteredFactionDirectoryPage({
    origin: "http://localhost:6217",
    applicationId: "dnd2024",
    stateSpaceId: "dnd2024-main",
    worldId: "world.caldris",
    fetchImpl: async (input) => {
      const request = new URL(input);
      calls.push(request.pathname + request.search);
      return response(200, {
        applicationId: "dnd2024",
        stateSpaceId: "dnd2024-main",
        qualifiedQueryId: factionDirectoryContract.id,
        stateSpaceFingerprint: "1".repeat(64),
        resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: factionDirectoryContract.outputSchemaHash,
        resultFingerprint: "4".repeat(64),
        sourceRevisionFingerprint: "A".repeat(64),
        data: {
          worldSummary: "A low-magic world shaped by roads, rivers, and rival powers.",
          items: [{
            id: "faction.caldris.tensor-sect", name: "Tensor Sect", status: "active",
            visibility: "public", summary: "A disciplined order.", goals: ["Perfect perception."],
            methods: ["Study."], assets: [], agenda: { state: "ready", summary: "Retrieve Ganji." },
            members: [{ id: "actor.caldris.ganji", name: "Ganji" }],
            controlledSites: [{ id: "location.caldris.ninth-angle", name: "House of the Ninth Angle" }],
            territories: [{ id: "location.caldris.highmead", name: "Highmead" }],
            allies: [], opponents: [],
          }],
          totalCount: 35, complete: false, nextCursor: "next-page",
        },
      });
    },
  });

  assert.equal(page.factions.length, 1);
  assert.equal(page.projection.resolutionFingerprint, "2".repeat(64));
  assert.equal(page.totalCount, 35);
  assert.equal(page.factions[0].agenda.summary, "Retrieve Ganji.");
  assert.deepEqual(page.factions[0].memberIds, ["actor.caldris.ganji"]);
  assert.deepEqual(page.factions[0].territoryIds,
    ["location.caldris.ninth-angle", "location.caldris.highmead"]);
  assert.equal(calls.length, 1);
  assert.match(calls[0], /dnd2024\.query\.faction-directory-page/u);
  assert.match(calls[0], /world\.caldris/u);
  assert.ok(calls.every((call) => !call.includes("knowledge") && !call.includes("inventory")));

  const denied = await readRegisteredFactionDirectoryPage({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    worldId: "world.caldris", fetchImpl: async () => response(403, {}),
  });
  assert.equal(denied, null);
});

const partyReference = (index) => ({
  id: `participation.${index}`, name: `Participation ${index}`, status: "active",
  actors: [{ id: `actor.${index}`, name: `Actor ${index}` }],
});

async function readRegisteredPartyBootstrap({
  party = [partyReference(0)], summary = {}, perspective = "dm", role = "game-master",
  queryResponse, localSeat,
} = {}) {
  const calls = [];
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217", requestedPerspective: perspective, localSeat,
    fetchImpl: async (input) => {
      const request = new URL(input); calls.push(request.pathname);
      if (request.pathname === "/api/audience-context") return response(200, {
        status: "bound", applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
        campaignId: "campaign.caldris.measure-of-mercy", role,
        ...(role === "actor" ? { actorId: "actor.0" } : {}),
      });
      if (role === "actor" && request.pathname.endsWith("/entities/actor.0"))
        return response(200, { entityId: "actor.0", name: "Actor 0" });
      if (request.pathname.includes("dnd2024.query.campaign-context")) {
        return response(200, {
          applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
          qualifiedQueryId: campaignContextContract.id,
          stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
          outputSchemaHash: campaignContextContract.outputSchemaHash, resultFingerprint: "3".repeat(64),
          sourceRevisionFingerprint: "4".repeat(64), data: {
            version: 1,
            campaignId: "campaign.caldris.measure-of-mercy",
            worldId: "world.caldris",
            campaign: { id: "campaign.caldris.measure-of-mercy", name: "The Measure of Mercy" },
            world: { id: "world.caldris", name: "Caldris" },
          },
        });
      }
      if (request.pathname.includes("dnd2024.query.campaign-summary")) {
        if (queryResponse) return queryResponse();
        return response(200, {
          applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
          qualifiedQueryId: campaignSummaryContract.id,
          stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
          outputSchemaHash: campaignSummaryContract.outputSchemaHash, resultFingerprint: "4".repeat(64),
          sourceRevisionFingerprint: "5".repeat(64), data: {
            status: "active", title: "The Measure of Mercy", premise: "Choose what mercy costs.",
            partyGoals: ["Protect the party."], toneAndBoundaries: ["No sexual violence."],
            party, totalCount: party.length, complete: true, nextCursor: null, ...summary,
          },
        });
      }
      assert.fail("Unexpected per-member or directory read: " + request.pathname);
    },
  });
  return { value, calls };
}

test("registered bootstrap keeps the server actor seat despite obsolete local DM input", async () => {
  const { value, calls } = await readRegisteredPartyBootstrap({
    role: "actor",
    perspective: "dm",
    localSeat: "dm",
    party: [{ ...partyReference(0), actors: [] }],
  });
  assert.deepEqual(value.audience, {
    seat: "player",
    perspective: "player",
    allowedPerspectives: ["player"],
  });
  assert.deepEqual(value.party.map((entry) => entry.id), ["actor.0"]);
  assert.equal(calls.length, 4);
  assert.ok(calls.some((path) => path.endsWith(
    "/entities/actor.0/read-models/dnd2024.query.campaign-context",
  )));
});

for (const count of [0, 1, 3, 20]) {
  test(`registered Campaign bootstrap batches all ${count} members in three HTTP reads`, async () => {
    const party = Array.from({ length: count }, (_, index) => partyReference(index));
    const { value, calls } = await readRegisteredPartyBootstrap({ party });
    assert.equal(value.status, "connected");
    assert.deepEqual(value.party.map(entry => ({ id: entry.id, name: entry.name })), party.flatMap(entry => entry.actors));
    assert.equal(value.campaign.projection.sourceRevisionFingerprint, "5".repeat(64));
    assert.equal(calls.length, 3);
    assert.ok(calls.every(path => !path.endsWith("/relationships") && !path.includes("/entities/actor.")));
    if (count) {
      const projected = connectedCampaignToHubEnvelope(value);
      assert.equal(projected.party[0].sheetState.status, "idle");
      assert.equal(projected.party[0].recordStatus, "Identity only");
    }
  });
}

function assertUnavailableRoster(value) {
  assert.equal(value.status, "unavailable");
  assert.equal("party" in value, false, "A failed join must not expose a partial roster");
  assert.equal(resolveHubSurface(value), "rules");
}

for (const failedIndex of [0, 19]) {
  for (const [reason, mutate] of Object.entries({
    "missing actor reference": entry => { entry.actors = []; },
    "missing actor field (old contract)": entry => { delete entry.actors; },
    "ambiguous actor references": entry => { entry.actors.push({ id: "actor.other", name: "Other" }); },
    "missing actor identity": entry => { delete entry.actors[0].id; },
    "missing actor name": entry => { delete entry.actors[0].name; },
    "extra private fields": entry => { entry.actors[0].secret = "Hidden"; },
    "invalid status": entry => { entry.status = "unknown"; },
  })) {
    test(`batched roster rejects ${reason} at participation ${failedIndex + 1}`, async () => {
      const party = Array.from({ length: 20 }, (_, index) => partyReference(index));
      mutate(party[failedIndex]);
      const { value, calls } = await readRegisteredPartyBootstrap({ party });
      assertUnavailableRoster(value);
      assert.equal(calls.length, 3);
    });
  }
}

test("batched roster fails closed on failed, denied and malformed object responses", async () => {
  for (const queryResponse of [
    () => response(500, {}), () => response(403, {}),
    () => { throw new Error("Transport failure"); },
    () => ({ ok: true, status: 200, json: async () => { throw new SyntaxError("Invalid JSON"); } }),
  ]) {
    const { value, calls } = await readRegisteredPartyBootstrap({ queryResponse });
    assertUnavailableRoster(value);
    assert.equal(calls.length, 3);
  }
});

test("batched roster rejects partial, over-limit and duplicated participation records", async () => {
  for (const options of [
    { summary: { totalCount: 2, complete: false, nextCursor: "more-members" } },
    { summary: { totalCount: 2, complete: true, nextCursor: null } },
    { party: Array.from({ length: 21 }, (_, index) => partyReference(index)) },
    { party: [partyReference(0), partyReference(0)] },
  ]) {
    const { value, calls } = await readRegisteredPartyBootstrap(options);
    assertUnavailableRoster(value);
    assert.equal(calls.length, 3);
  }
});

test("batched roster deduplicates shared actors, excludes withdrawn participants and rejects conflicting names", async () => {
  const duplicate = { ...partyReference(1), actors: partyReference(0).actors };
  const withdrawn = { ...partyReference(2), status: "withdrawn", actors: [] };
  const { value, calls } = await readRegisteredPartyBootstrap({ party: [partyReference(0), duplicate, withdrawn] });
  assert.equal(value.status, "connected");
  assert.deepEqual(value.party.map(entry => entry.id), ["actor.0"]);
  assert.equal(calls.length, 3);
  const empty = await readRegisteredPartyBootstrap({ party: [withdrawn] });
  assert.deepEqual(empty.value.party, []);
  duplicate.actors = [{ id: "actor.0", name: "Conflicting name" }];
  assertUnavailableRoster((await readRegisteredPartyBootstrap({ party: [partyReference(0), duplicate] })).value);
});

for (const role of ["game-master", "actor"]) {
  test(`batched ${role} Player projection never accepts DM actor references`, async () => {
    const party = Array.from({ length: 20 }, (_, index) => ({ ...partyReference(index), actors: [] }));
    const { value, calls } = await readRegisteredPartyBootstrap({ party, role, perspective: "player" });
    assert.equal(value.status, "connected");
    assert.equal(calls.length, role === "actor" ? 4 : 3);
    assert.ok(value.campaign.party.every(entry => entry.actors.length === 0));
    if (role === "actor") assert.deepEqual(value.party.map(entry => entry.id), ["actor.0"]);
    else assert.ok(value.party.every(entry => entry.id.startsWith("participation.")));
    party[19].actors = [{ id: "actor.secret", name: "Secret" }];
    assertUnavailableRoster((await readRegisteredPartyBootstrap({ party, role, perspective: "player" })).value);
  });
}

const MEDIA_HASH = "3ae0336e89155a4a00fb0d982ae903bf9ed1137cd292b097b252fd38c1501fa3";

function dossierDefinition(reference, kind, status = "active") {
  return {
    id: reference.id,
    label: reference.label,
    canonicalName: reference.label,
    kind,
    status,
    summary: null,
    source: status === "active"
      ? { sourceId: "dnd2024.source.srd-5.2.1", locator: `Fixture > ${reference.label}` }
      : null,
  };
}

function characterDossier(sheet) {
  const species = dossierDefinition(sheet.origin.species, "species");
  const background = dossierDefinition(sheet.origin.background, "background");
  const classes = sheet.classes.map((entry) => ({
    id: entry.id,
    name: entry.name,
    definition: dossierDefinition(entry.class, "class"),
    level: entry.level,
    subclass: entry.subclass,
  }));
  const inventoryDefinitions = sheet.inventory.items.map((entry) =>
    dossierDefinition(entry.definition, "equipment", "identity-only"));
  const definitions = [species, background, ...classes.map((entry) => entry.definition), ...inventoryDefinitions];
  return {
    version: 1,
    sheet,
    origin: { species, background, traits: [] },
    classes,
    features: [],
    inventory: { definitions: inventoryDefinitions, contentsDepth: 4, mayOmitDeeperContents: true },
    levelOneRules: {
      test: "character-level-one-rules-project",
      subjectId: sheet.subject.id,
      armorClass: {},
      attacks: [],
      senses: [],
      savingThrowCircumstances: [],
      spellAccess: {},
      equipment: {},
      entitlements: [],
    },
    definitions,
    provenance: {
      sheetQueryId: "dnd2024.query.character-sheet-v2",
      sheetProjectionId: "dnd2024.mechanic.character-sheet-v2.project",
      dossierProjectionId: "dnd2024.mechanic.character-dossier-v1.project",
      definitionCount: definitions.length,
      inventoryDepth: 4,
      ruleTextPolicy: "canonical-only",
    },
  };
}

function mediaAttachment(role = "portrait", alt = "A reviewed portrait", mediaId = "visual-0") {
  return {
    mediaId,
    role,
    mediaType: "image/png",
    width: 1024,
    height: 1536,
    alt,
    caption: "",
    order: 0,
    contentUrl: `/api/applications/dnd2024/state-spaces/dnd2024-main/entities/owner/media/${mediaId}/content`,
  };
}

test("character sheet resource reads the existing calculated query without dossier or media fan-out", async () => {
  const sheet = {
    version: 2, subject: { id: "actor.fixture", label: "Fixture Hero" },
    origin: { species: { id: "species.fixture", label: "Fixture Species" },
      background: { id: "background.fixture", label: "Fixture Background" } },
    classes: [{ id: "membership.fixture", name: "Fixture membership",
      class: { id: "class.fixture", label: "Fixture Class" }, level: 1, subclass: null }],
    inventory: { items: [], contentsDepth: 4, mayOmitDeeperContents: true },
    wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
  };
  const calls = [];
  const result = await readCanonicalCharacterSheet({
    origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "fixture",
    actorId: "actor.fixture", perspective: "player",
    fetchImpl: async (input) => {
      calls.push(new URL(input));
      return response(200, { applicationId: "dnd2024", stateSpaceId: "fixture",
        qualifiedQueryId: characterSheetContract.id, outputSchemaHash: characterSheetContract.outputSchemaHash,
        stateSpaceFingerprint: "A".repeat(64), resolutionFingerprint: "B".repeat(64),
        resultFingerprint: "C".repeat(64), sourceRevisionFingerprint: "D".repeat(64), data: sheet });
    },
  });
  assert.equal(result.status, "ready");
  assert.equal(result.data.subject.id, "actor.fixture");
  assert.equal(calls.length, 1);
  assert.match(calls[0].pathname, /\/entities\/actor\.fixture\/read-models\/dnd2024\.query\.character-sheet-v2$/u);
  assert.equal(calls[0].searchParams.get("perspective"), "player");
});

test("canonical senses use named references and reject legacy or partial measurements before rendering", async () => {
  const namedSense = { sense: { id: "dnd2024.vocabulary.sense.darkvision", label: "Darkvision" },
    numerator: 60, denominator: 1, unit: { id: "dnd2024.vocabulary.distance-unit.foot", label: "Foot" } };
  const cases = [
    { sense: namedSense, ready: true },
    { sense: { sense: namedSense.sense }, ready: true },
    { sense: { id: namedSense.sense.id, numerator: 60, denominator: 1, unitId: namedSense.unit.id }, ready: false },
    { sense: { ...namedSense, unit: undefined }, ready: false },
    { sense: { ...namedSense, denominator: 0 }, ready: false },
    { sense: { ...namedSense, sense: { id: namedSense.sense.id } }, ready: false },
    { sense: null, ready: false },
  ];
  for (const item of cases) {
    const data = characterDossier({
      version: 2, subject: { id: "actor.fixture", label: "Fixture Hero" },
      origin: { species: { id: "species.fixture", label: "Fixture Species" }, background: { id: "background.fixture", label: "Fixture Background" } },
      classes: [{ id: "membership.fixture", name: "Fixture membership", class: { id: "class.fixture", label: "Fixture Class" }, level: 1, subclass: null }],
      senses: [item.sense], inventory: { items: [], contentsDepth: 4, mayOmitDeeperContents: true },
      wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
    });
    const result = await readCanonicalCharacter({
      origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "fixture", actorId: "actor.fixture",
      fetchImpl: async () => response(200, { applicationId: "dnd2024", stateSpaceId: "fixture",
        qualifiedQueryId: characterDossierContract.id, outputSchemaHash: characterDossierContract.outputSchemaHash,
        stateSpaceFingerprint: "A".repeat(64), resolutionFingerprint: "B".repeat(64),
        resultFingerprint: "C".repeat(64), sourceRevisionFingerprint: "D".repeat(64), data }),
    });
    assert.equal(result.status, item.ready ? "ready" : "error", JSON.stringify(item.sense));
    if (item.ready) assert.deepEqual(result.data.senses, [item.sense]);
    else {
      assert.equal(result.failureCategory, "incompatible-data");
      assert.equal(result.data, null);
    }
  }
});

function mediaRecord(...attachments) {
  return {
    applicationId: "dnd2024",
    stateSpaceId: "dnd2024-main",
    entityId: "owner",
    resolutionFingerprint: "fixture",
    attachments,
  };
}

test("visual media consumes only owner-authorized discovery and returns no private blob metadata", () => {
  const record = mediaRecord(mediaAttachment("portrait", "Player portrait"));
  assert.deepEqual(projectMediaVisual(record), {
    portrait: {
      imageUrl: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/owner/media/visual-0/content",
      alt: "Player portrait",
      width: 1024,
      height: 1536,
    },
  });
  const serialized = JSON.stringify(projectMediaVisual(record));
  assert.equal(serialized.includes(MEDIA_HASH), false);
  assert.equal(serialized.includes("provenance"), false);
});

test("item media inherits definition roles while explicit instance roles win", () => {
  const definitionIcon = {
    imageUrl: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/definition/media/icon/content",
    alt: "Definition icon", width: 64, height: 64,
  };
  const definitionIllustration = {
    imageUrl: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/definition/media/illustration/content",
    alt: "Definition illustration", width: 600, height: 800,
  };
  const instanceIcon = {
    imageUrl: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/instance/media/icon/content",
    alt: "Instance icon", width: 64, height: 64,
  };
  const inherited = inheritMediaVisual(
    { icon: instanceIcon, gallery: [{ ...instanceIcon, mediaId: "visual-0", role: "icon", caption: "" }] },
    {
      icon: definitionIcon,
      illustration: definitionIllustration,
      gallery: [
        { ...definitionIcon, mediaId: "visual-0", role: "icon", caption: "" },
        { ...definitionIllustration, mediaId: "visual-1", role: "illustration", caption: "" },
      ],
    },
  );

  assert.deepEqual(inherited.icon, instanceIcon);
  assert.deepEqual(inherited.illustration, definitionIllustration);
  assert.deepEqual(inherited.gallery.map((entry) => [entry.role, entry.alt]), [
    ["icon", "Instance icon"],
    ["illustration", "Definition illustration"],
  ]);
});

test("visual media fails closed on malformed or non-owner-bound discovery", () => {
  assert.equal(projectMediaVisual(mediaRecord()), null);
  assert.equal(projectMediaVisual(mediaRecord({ ...mediaAttachment(), contentUrl: "/components/media/file.png" })), null);
  assert.equal(projectMediaVisual(mediaRecord({ ...mediaAttachment(), mediaType: "image/svg+xml" })), null);
  assert.equal(projectMediaVisual({ attachments: [{ ...mediaAttachment(), injected: true }] }), null);
});

test("visual media preserves an ordered gallery when an entity has several authorized images", () => {
  const second = { ...mediaAttachment("scene", "Night at the market", "visual-1"), order: 2 };
  const first = { ...mediaAttachment("setting", "The market at dawn", "visual-0"), order: 1 };
  const projected = projectMediaVisual(mediaRecord(second, first));

  assert.deepEqual(projected.gallery.map((entry) => [entry.mediaId, entry.role, entry.alt]), [
    ["visual-0", "setting", "The market at dawn"],
    ["visual-1", "scene", "Night at the market"],
  ]);
});

test("current scene records resolve encounter then conversation then exploration", () => {
  const locations = ["location.thalorien.brackenford"];
  assert.deepEqual(resolveCurrentSceneRecord({
    location: { entityId: locations[0] },
    conversation: { entityId: "interaction.brackenford.parley" },
    encounter: { entityId: "encounter.brackenford.ambush" },
  }, locations), {
    kind: "combat",
    locationId: locations[0],
    conversationId: "interaction.brackenford.parley",
    encounterId: "encounter.brackenford.ambush",
  });
  assert.deepEqual(resolveCurrentSceneRecord({
    location: { entityId: locations[0] },
    conversation: { entityId: "interaction.brackenford.parley" },
  }, locations), {
    kind: "conversation",
    locationId: locations[0],
    conversationId: "interaction.brackenford.parley",
  });
  assert.deepEqual(resolveCurrentSceneRecord({ location: { entityId: locations[0] } }, locations), {
    kind: "exploration",
    locationId: locations[0],
  });
});

test("current scene records reject unknown locations and open or malformed references", () => {
  const locations = ["location.thalorien.brackenford"];
  assert.equal(resolveCurrentSceneRecord({
    location: { entityId: "location.thalorien.hidden" },
  }, locations), null);
  assert.equal(resolveCurrentSceneRecord({
    location: { entityId: locations[0] },
    conversation: { entityId: "interaction.brackenford.parley", name: "Injected" },
  }, locations), null);
  assert.equal(resolveCurrentSceneRecord({
    location: { entityId: locations[0] },
    guessedMode: "combat",
  }, locations), null);
});

test("recorded play situations preserve continuity without becoming authoritative ECS scenes", () => {
  const locationId = "location.thalorien.brackenford";
  assert.deepEqual(resolveRecordedPlaySituation({
    recentMessages: [
      { id: "play-message.1", ordinal: 1, role: "player", text: "I ask about the road." },
      { id: "play-message.2", ordinal: 2, role: "assistant", text: "Tibb answers word for word." },
    ],
    currentSituation: {
      id: "play-situation.1",
      status: "active",
      kind: "conversation",
      summary: "Orban asks Tibb about the closed northern road.",
      participants: [
        { name: "Orban", entityId: "actor.thalorien.brackenford.orban" },
        { name: "Tibb Fallow", entityId: null },
      ],
      location: { name: "Brackenford", entityId: locationId },
    },
  }, [locationId]), {
    status: "ready",
    kind: "recorded",
    locationId,
    recorded: {
      id: "play-situation.1",
      kind: "conversation",
      summary: "Orban asks Tibb about the closed northern road.",
      participants: [
        { id: "actor.thalorien.brackenford.orban", name: "Orban", entityId: "actor.thalorien.brackenford.orban" },
        { id: "play-situation.1.participant.2", name: "Tibb Fallow" },
      ],
      interactions: [
        { id: "play-message.1", ordinal: 1, role: "player", text: "I ask about the road." },
        { id: "play-message.2", ordinal: 2, role: "assistant", text: "Tibb answers word for word." },
      ],
      location: { id: locationId, name: "Brackenford" },
    },
  });
  assert.equal(resolveRecordedPlaySituation({
    recentMessages: [],
    currentSituation: { id: "play-situation.2", status: "active", kind: "initiative", summary: "No.", participants: [] },
  }, []), null);
});

test("scene affordances match the full current scene and filter GM-only context", () => {
  const currentScene = {
    kind: "combat",
    locationId: "location.thalorien.brackenford",
    conversationId: "interaction.brackenford.parley",
    encounterId: "encounter.brackenford.ambush",
  };
  const record = {
    scene: {
      location: { entityId: currentScene.locationId },
      conversation: { entityId: currentScene.conversationId },
      encounter: { entityId: currentScene.encounterId },
    },
    items: [
      { key: "take-cover", label: "Take cover", summary: "Move behind the ruined wall.", visibility: "party" },
      { key: "spring-ambush", label: "Spring the ambush", summary: "Reveal the hidden archers.", visibility: "gm" },
    ],
  };

  assert.deepEqual(resolveSceneAffordancesRecord(record, currentScene, "player"), [
    { key: "take-cover", label: "Take cover", summary: "Move behind the ruined wall." },
  ]);
  assert.deepEqual(resolveSceneAffordancesRecord(record, currentScene, "dm"), [
    { key: "take-cover", label: "Take cover", summary: "Move behind the ruined wall." },
    { key: "spring-ambush", label: "Spring the ambush", summary: "Reveal the hidden archers." },
  ]);
});

test("scene affordances fail closed for stale selectors and duplicate keys", () => {
  const currentScene = {
    kind: "conversation",
    locationId: "location.thalorien.brackenford",
    conversationId: "interaction.brackenford.parley",
  };
  const item = { key: "ask-about-road", label: "Ask about the road", summary: "Learn what lies ahead.", visibility: "party" };
  assert.equal(resolveSceneAffordancesRecord({
    scene: {
      location: { entityId: currentScene.locationId },
      conversation: { entityId: "interaction.brackenford.stale" },
    },
    items: [item],
  }, currentScene, "player"), null);
  assert.equal(resolveSceneAffordancesRecord({
    scene: {
      location: { entityId: currentScene.locationId },
      conversation: { entityId: currentScene.conversationId },
    },
    items: [item, { ...item, label: "Duplicate" }],
  }, currentScene, "player"), null);
  assert.equal(resolveSceneAffordancesRecord({
    scene: {
      location: { entityId: currentScene.locationId },
      conversation: { entityId: currentScene.conversationId },
    },
    items: [{ ...item, summary: "   " }],
  }, currentScene, "player"), null);
});

test("known ways onward require admitted exact route and destination subjects", async () => {
  const routeId = "route.thalorien.brackenford-to-crownmere";
  const originId = "location.thalorien.brackenford";
  const destinationId = "location.thalorien.crownmere";
  const requestedKinds = [];
  const routes = await readKnownOpenRoutes({
    fetchImpl: async (input) => {
      const requested = new URL(input);
      const path = requested.pathname;
      if (path.endsWith(`/${routeId}/components/game.core.world.route`)) {
        requestedKinds.push("game.core.world.route");
        return response(200, {
          entityId: routeId,
          qualifiedTypeId: "game.core.world.route",
          valueJson: JSON.stringify({
            status: "active",
            summary: "CANARY GM ROUTE SUMMARY",
            visibility: "gm",
            mode: "on-foot",
            durationMinutes: 45,
          }),
        });
      }
      if (path.endsWith(`/${routeId}/components/game.core.world.route.availability`)) {
        requestedKinds.push("game.core.world.route.availability");
        return response(200, {
          entityId: routeId,
          qualifiedTypeId: "game.core.world.route.availability",
          valueJson: JSON.stringify({ status: "open" }),
        });
      }
      if (path.endsWith(`/${destinationId}/components/game.core.world.location`)) {
        return response(200, {
          entityId: destinationId,
          qualifiedTypeId: "game.core.world.location",
          valueJson: JSON.stringify({
            kind: "settlement", status: "active", summary: "A known port.", visibility: "public",
          }),
        });
      }
      if (path.endsWith("/relationships")) {
        const kind = requested.searchParams.get("qualifiedKind");
        requestedKinds.push(kind);
        const targets = {
          "game.core.world.route.in-world": "world.thalorien",
          "game.core.world.route.from": originId,
          "game.core.world.route.to": destinationId,
        };
        return response(200, { items: [{ fromEntityId: routeId, toEntityId: targets[kind], qualifiedKind: kind }] });
      }
      return response(404, {});
    },
    origin: "http://localhost:6217",
    entityRoot: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities",
    worldId: "world.thalorien",
    currentLocationId: originId,
    perspective: "player",
    projectedKnowledge: {
      status: "ready",
      entries: [
        { text: "The Crownmere road is open.", stance: "known", presentationKind: "statement",
          subject: { id: routeId, name: "Crownmere road" } },
        { text: "Crownmere is a known port.", stance: "known", presentationKind: "statement",
          subject: { id: destinationId, name: "Crownmere" } },
      ],
      locations: [],
    },
    locationDirectory: [{ id: originId, name: "Brackenford" }, { id: destinationId, name: "Crownmere" }],
  });

  assert.deepEqual(routes, [{
    id: routeId,
    originId,
    destinationId,
    destinationName: "Crownmere",
    detail: "The Crownmere road is open.",
    mode: "on-foot",
    durationMinutes: 45,
  }]);
  assert.equal(JSON.stringify(routes).includes("CANARY"), false);
  assert.equal(requestedKinds.every((kind) => kind.startsWith("game.core.")), true);
});

test("known ways onward fail closed without destination knowledge", async () => {
  const routeId = "route.thalorien.brackenford-to-crownmere";
  const originId = "location.thalorien.brackenford";
  const destinationId = "location.thalorien.crownmere";
  const routes = await readKnownOpenRoutes({
    fetchImpl: async (input) => {
      const requested = new URL(input);
      const path = requested.pathname;
      if (path.endsWith(`/${routeId}/components/game.core.world.route`)) {
        return response(200, {
          entityId: routeId,
          qualifiedTypeId: "game.core.world.route",
          valueJson: JSON.stringify({
            status: "active", summary: "A road.", visibility: "public", mode: "on-foot", durationMinutes: 45,
          }),
        });
      }
      if (path.endsWith(`/${routeId}/components/game.core.world.route.availability`)) {
        return response(200, {
          entityId: routeId,
          qualifiedTypeId: "game.core.world.route.availability",
          valueJson: JSON.stringify({ status: "open" }),
        });
      }
      if (path.endsWith("/relationships")) {
        const kind = requested.searchParams.get("qualifiedKind");
        const targets = {
          "game.core.world.route.in-world": "world.thalorien",
          "game.core.world.route.from": originId,
          "game.core.world.route.to": destinationId,
        };
        return response(200, { items: [{ fromEntityId: routeId, toEntityId: targets[kind], qualifiedKind: kind }] });
      }
      return response(404, {});
    },
    origin: "http://localhost:6217",
    entityRoot: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities",
    worldId: "world.thalorien",
    currentLocationId: originId,
    perspective: "player",
    projectedKnowledge: {
      status: "ready",
      entries: [{
        text: "The road is known.", stance: "known", presentationKind: "statement",
        subject: { id: routeId, name: "Road" },
      }],
      locations: [],
    },
    locationDirectory: [{ id: originId, name: "Brackenford" }, { id: destinationId, name: "Crownmere" }],
  });
  assert.deepEqual(routes, []);
});

test("conversation current scene excludes unapproved participants and summary from Player", async () => {
  const entityRoot = "/api/applications/dnd2024/state-spaces/dnd2024-main/entities";
  const value = await readConversationCurrentScene({
    fetchImpl: async (input) => {
      const requested = new URL(input);
      if (requested.pathname.endsWith("/interaction.brackenford.parley")) {
        return response(200, { entityId: "interaction.brackenford.parley", name: "Gatehouse parley" });
      }
      if (requested.pathname.endsWith("/components/game.core.world.interaction")) {
        return response(200, {
          entityId: "interaction.brackenford.parley",
          qualifiedTypeId: "game.core.world.interaction",
          valueJson: JSON.stringify({
            kind: "conversation",
            status: "accepted",
            summary: "CANARY DM CONVERSATION SUMMARY",
          }),
        });
      }
      if (requested.pathname.endsWith("/relationships")) {
        return response(200, { items: [
          {
            fromEntityId: "interaction.brackenford.parley",
            toEntityId: "actor.hero",
            qualifiedKind: "game.core.world.interaction.participant",
          },
          {
            fromEntityId: "interaction.brackenford.parley",
            toEntityId: "actor.secret-npc",
            qualifiedKind: "game.core.world.interaction.participant",
          },
        ] });
      }
      if (requested.pathname.endsWith("/actor.hero")) {
        return response(200, { entityId: "actor.hero", name: "Hero" });
      }
      throw new Error(`Unexpected request ${requested}`);
    },
    origin: "http://localhost:6217",
    entityRoot,
    conversationId: "interaction.brackenford.parley",
    perspective: "player",
    authorizedActorIds: new Set(["actor.hero"]),
  });
  assert.deepEqual(value, {
    status: "ready",
    kind: "conversation",
    conversation: {
      id: "interaction.brackenford.parley",
      name: "Gatehouse parley",
      participants: [{ id: "actor.hero", name: "Hero" }],
    },
  });
  assert.equal(JSON.stringify(value).includes("CANARY"), false);
  assert.equal(JSON.stringify(value).includes("secret-npc"), false);
});

test("combat current scene reads exact locked Initiative without inventing a turn", async () => {
  const entityRoot = "/api/applications/dnd2024/state-spaces/dnd2024-main/entities";
  const encounterId = "encounter.brackenford.ambush";
  const participationId = "participation.brackenford.hero";
  const value = await readCombatCurrentScene({
    fetchImpl: async (input) => {
      const requested = new URL(input);
      const kind = requested.searchParams.get("qualifiedKind");
      if (requested.pathname.endsWith(`/entities/${encounterId}`)) {
        return response(200, { entityId: encounterId, name: "Brackenford ambush" });
      }
      if (requested.pathname.endsWith(`/entities/${encounterId}/components/dnd2024.encounter.definition`)) {
        return response(200, {
          entityId: encounterId,
          qualifiedTypeId: "dnd2024.encounter.definition",
          valueJson: JSON.stringify({ environment: { entityId: "location.thalorien.brackenford" } }),
        });
      }
      if (requested.pathname.endsWith(`/entities/${encounterId}/read-models/dnd2024.query.encounter-board`)) {
        assert.equal(requested.searchParams.get("perspective"), "player");
        return response(200, boardEnvelope());
      }
      if (requested.pathname.endsWith("/relationships") && kind === "dnd2024.encounter.has-participation") {
        return response(200, { items: [{
          fromEntityId: encounterId,
          toEntityId: participationId,
          qualifiedKind: kind,
        }] });
      }
      if (requested.pathname.endsWith("/relationships") &&
          ["dnd2024.encounter.active-round", "dnd2024.encounter.active-turn"].includes(kind)) {
        return response(200, { items: [] });
      }
      if (requested.pathname.endsWith(`/entities/${participationId}/components/dnd2024.encounter.participation`)) {
        return response(200, {
          entityId: participationId,
          qualifiedTypeId: "dnd2024.encounter.participation",
          valueJson: JSON.stringify({
            membershipRelationship: {
              stateSpaceId: "dnd2024-main",
              fromEntityId: encounterId,
              toEntityId: participationId,
              qualifiedKind: "dnd2024.encounter.has-participation",
            },
            status: "active",
          }),
        });
      }
      if (requested.pathname.endsWith(`/entities/${participationId}/components/dnd2024.combat.initiative`)) {
        return response(200, {
          entityId: participationId,
          qualifiedTypeId: "dnd2024.combat.initiative",
          valueJson: JSON.stringify({
            encounter: { entityId: encounterId }, status: "locked", result: 17, tieBreakOrder: 0,
          }),
        });
      }
      if (requested.pathname.endsWith(`/entities/${participationId}/components/dnd2024.combat.position`)) {
        return response(200, {
          entityId: participationId,
          qualifiedTypeId: "dnd2024.combat.position",
          valueJson: JSON.stringify({
            encounter: { entityId: encounterId },
            anchor: { x: 2, y: 3 },
            footprint: { width: 2, height: 1 },
            elevationFeet: 5,
            visibility: "public",
            revision: 4,
          }),
        });
      }
      if (requested.pathname.endsWith("/relationships") &&
          kind === "dnd2024.encounter.participation.for-actor") {
        return response(200, { items: [{
          fromEntityId: participationId,
          toEntityId: "actor.hero",
          qualifiedKind: kind,
        }] });
      }
      if (requested.pathname.endsWith("/entities/actor.hero")) {
        return response(200, { entityId: "actor.hero", name: "Hero" });
      }
      throw new Error(`Unexpected request ${requested}`);
    },
    origin: "http://localhost:6217",
    entityRoot,
    encounterId,
    stateSpaceId: "dnd2024-main",
    perspective: "player",
    authorizedActorIds: new Set(["actor.hero"]),
  });
  assert.deepEqual(value, {
    status: "ready",
    kind: "combat",
    combat: {
      id: encounterId,
      name: "Brackenford ambush",
      participants: [{ id: "actor.hero", name: "Hero", initiative: 17, active: false }],
      board: {
        revision: 7,
        columns: 12,
        rows: 8,
        feetPerSquare: 5,
        terrain: [{ id: "terrain.rubble", label: "Rubble", area: { x: 4, y: 2, width: 2, height: 1 }, movementCost: 2 }],
        obstacles: [{ id: "obstacle.wall", label: "Wall", area: { x: 6, y: 1, width: 1, height: 3 } }],
        participants: [{
          id: participationId,
          name: "Hero",
          initiative: 17,
          active: false,
          position: { x: 2, y: 3, width: 2, height: 1, elevationFeet: 5, revision: 4 },
        }],
      },
    },
  });
});

function response(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

test("canonical character reads distinguish stale fingerprints, HTTP, authorization, transport, and incompatible data", async () => {
  const common = {
    origin: "http://localhost:6217",
    applicationId: "dnd2024",
    stateSpaceId: "dnd2024-main",
    actorId: "actor.test.hero",
  };
  const serverError = await readCanonicalCharacter({
    ...common,
    fetchImpl: async () => new Response("{}", {
      status: 500,
      headers: { "x-request-id": "request-500" },
    }),
  });
  assert.deepEqual(serverError, {
    status: "error",
    data: null,
    failureCategory: "http",
    diagnosticId: "request-500",
    httpStatus: 500,
  });

  const forbidden = await readCanonicalCharacter({
    ...common,
    fetchImpl: async () => response(403, {}),
  });
  assert.equal(forbidden.status, "forbidden");
  assert.equal(forbidden.failureCategory, "authorization");

  const stale = await readCanonicalCharacter({
    ...common,
    fetchImpl: async () => response(409, {
      code: "READ_MODEL_STATE_SPACE_STALE",
      message: "The state space is not bound to the current application resolution.",
    }),
  });
  assert.equal(stale.status, "error");
  assert.equal(stale.failureCategory, "stale-data");
  assert.equal(stale.errorCode, "READ_MODEL_STATE_SPACE_STALE");
  assert.equal(stale.httpStatus, 409);

  const incompatible = await readCanonicalCharacter({
    ...common,
    fetchImpl: async () => response(200, { data: { version: 1 } }),
  });
  assert.equal(incompatible.status, "error");
  assert.equal(incompatible.failureCategory, "incompatible-data");
  assert.equal(incompatible.data, null);

  const transport = await readCanonicalCharacter({
    ...common,
    fetchImpl: async () => { throw new Error("offline"); },
  });
  assert.equal(transport.status, "error");
  assert.equal(transport.failureCategory, "transport");
});

test("normalizes only a credential-free HTTP(S) server origin", () => {
  assert.equal(normalizeGameServerOrigin("http://localhost:6217"), "http://localhost:6217");
  assert.equal(normalizeGameServerOrigin("https://table.example.test/"), "https://table.example.test");
  assert.equal(normalizeGameServerOrigin("http://user@example.test"), null);
  assert.equal(normalizeGameServerOrigin("http://example.test/api"), null);
  assert.equal(normalizeGameServerOrigin("file:///campaign"), null);
});

test("resolves only the ambient actor's exact authorized presence location", () => {
  const actorId = "actor.thalorien.brackenford.orban";
  const locations = ["location.thalorien.brackenford"];
  assert.equal(resolvePresenceLocation({ containment: {
    containedEntityId: actorId,
    containerEntityId: "location.thalorien.brackenford",
    slot: "presence",
  } }, actorId, locations), "location.thalorien.brackenford");
  assert.equal(resolvePresenceLocation({ containment: {
    containedEntityId: "actor.thalorien.someone-else",
    containerEntityId: "location.thalorien.brackenford",
    slot: "presence",
  } }, actorId, locations), null);
  assert.equal(resolvePresenceLocation({ containment: {
    containedEntityId: actorId,
    containerEntityId: "location.thalorien.brackenford",
    slot: "party",
  } }, actorId, locations), null);
  assert.equal(resolvePresenceLocation({ containment: {
    containedEntityId: actorId,
    containerEntityId: "location.thalorien.crownmere",
    slot: "presence",
  } }, actorId, locations), null);
});

test("does not read campaign state after an audience denial", async () => {
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    fetchImpl: async () => response(403, { status: "denied", error: "AUDIENCE_CONTEXT_DENIED" }),
  });

  assert.deepEqual(value, {
    version: 1,
    status: "denied",
    message: "The game server did not authorize a campaign for this local table.",
  });
});

test("rejects an actor's cross-campaign request before reading campaign detail", async () => {
  const calls = [];
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    requestedCampaignId: "campaign.embersea.black-tide",
    fetchImpl: async (input) => {
      const path = new URL(input).pathname;
      calls.push(path);
      if (path === "/api/audience-context") {
        return response(200, {
          status: "bound",
          applicationId: "dnd2024",
          stateSpaceId: "dnd2024-main",
          campaignId: "campaign.thalorien.brackenford",
          actorId: "actor.thalorien.brackenford.orban",
          role: "actor",
        });
      }
      throw new Error(`Unexpected request ${path}`);
    },
  });

  assert.deepEqual(value, {
    version: 1,
    status: "denied",
    message: "That campaign is not available to this local table.",
  });
  assert.deepEqual(calls, ["/api/audience-context"]);
});
