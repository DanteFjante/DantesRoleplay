import assert from "node:assert/strict";
import test from "node:test";
import { contract as characterSheetContract } from "../src/server/character-sheet-contract.js";
import { contract as characterDossierContract } from "../src/server/character-dossier-contract.js";
import { readCanonicalCharacter, readCanonicalCharacterSheet } from "../src/server/game-server-context.js";
import { projectCharacterDetails, projectCharacterSheet } from "../src/features/character/project-character.ts";

const scope = {
  origin: "http://localhost:6217", applicationId: "dnd2024", stateSpaceId: "fixture",
  actorId: "actor.synthetic", perspective: "player",
};

function envelope(contract, data, overrides = {}) {
  return {
    applicationId: "dnd2024", stateSpaceId: "fixture", qualifiedQueryId: contract.id,
    outputSchemaHash: contract.outputSchemaHash, stateSpaceFingerprint: "A".repeat(64),
    resolutionFingerprint: "B".repeat(64), resultFingerprint: "C".repeat(64),
    sourceRevisionFingerprint: "D".repeat(64), data, ...overrides,
  };
}

function ref(id, label = id) { return { id, label }; }

function sheet(overrides = {}) {
  return {
    version: 2,
    subject: ref("actor.synthetic", "Synthetic Hero"),
    identity: { biography: "A bounded synthetic biography." },
    origin: { species: ref("species.synthetic", "Synthetic Species"), background: ref("background.synthetic", "Synthetic Background") },
    classes: [{ id: "class-entry.synthetic", name: "Synthetic class", class: ref("class.synthetic", "Synthetic Class"), level: 1, subclass: null }],
    abilities: [{ ability: ref("ability.synthetic", "Synthetic Ability"), score: 10, modifier: 0 }],
    hitPoints: { current: 7, maximum: 10, maximumReduction: 0 },
    inventory: { items: [], contentsDepth: 4, mayOmitDeeperContents: true },
    wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
    ...overrides,
  };
}

function definition(id, label = id) {
  return { id, label, canonicalName: label, kind: "feature", status: "active",
    summary: `${label} description.`, source: { sourceId: "source.synthetic", locator: `Synthetic > ${label}` } };
}

function dossierFields(base = sheet()) {
  const species = definition("species.synthetic", "Synthetic Species");
  const background = definition("background.synthetic", "Synthetic Background");
  const classDefinition = definition("class.synthetic", "Synthetic Class");
  const featureDefinition = definition("feature.synthetic", "Synthetic Feature");
  const classes = [{ id: "class-entry.synthetic", name: "Synthetic class", definition: classDefinition,
    level: 1, subclass: null }];
  const features = [{ definition: featureDefinition, grantedBy: classDefinition, grantKind: "feat",
    classLevel: 1, configurationKey: null,
    implementation: { status: "executable", reason: null, entitlementKey: "feature.synthetic",
      nextCapabilityId: null } }];
  const definitions = [species, background, classDefinition, featureDefinition];
  return {
    version: 1, sheet: base,
    origin: { species, background, traits: [{ key: "trait.synthetic", label: "Synthetic Trait",
      status: "active", reason: null, mechanicId: "mechanic.synthetic", source: null }] },
    classes, features,
    inventory: { definitions: [], contentsDepth: 4, mayOmitDeeperContents: true },
    levelOneRules: { test: "character-level-one-rules-project", subjectId: base.subject.id,
      armorClass: {}, attacks: [], senses: [], savingThrowCircumstances: [], spellAccess: {},
      equipment: {}, entitlements: [] },
    definitions,
    provenance: { sheetQueryId: "dnd2024.query.character-sheet-v2",
      sheetProjectionId: "dnd2024.mechanic.character-sheet-v2.project",
      dossierProjectionId: "dnd2024.mechanic.character-dossier-v1.project",
      definitionCount: definitions.length, inventoryDepth: 4, ruleTextPolicy: "canonical-only" },
  };
}

function reader(contract, data, calls = []) {
  return {
    ...scope,
    fetchImpl: async (input) => {
      calls.push(new URL(input).pathname);
      return new Response(JSON.stringify(envelope(contract, data)), { status: 200,
        headers: { "content-type": "application/json" } });
    },
  };
}

test("T01/T03/T04/T05/T08 sheet consumes useful fields independently of malformed optional sections", async () => {
  const value = sheet({
    unknownFutureField: { executable: true },
    abilities: [
      { ability: ref("ability.good", "Good Ability"), score: 12, modifier: 1 },
      { ability: ref("ability.bad", "Bad Ability"), score: "not-a-score", modifier: 99 },
    ],
    savingThrows: [{ ability: ref("ability.good", "Good Ability"), proficient: false, modifier: 0 }],
    temporaryHitPoints: { amount: 0 },
    hitPoints: { current: 50, maximum: 4, maximumReduction: 0 },
    spellcasting: [{ id: "spell.bad", name: "Bad spell", sourceDefinition: {}, ability: ref("ability.good"), preparedSpells: [], availableSpells: [] }],
    features: [{ feature: {}, grantedBy: {}, grantKind: {}, classLevel: "unknown" }],
    narrative: { privateRuleText: "must not be rendered" },
    inventory: { malformed: true },
    wallet: { malformed: true },
  });
  const result = await readCanonicalCharacterSheet(reader(characterSheetContract, value));
  assert.equal(result.status, "ready");
  assert.equal(result.data.subject.id, "actor.synthetic");
  assert.equal(result.data.identity.biography, "A bounded synthetic biography.");
  assert.deepEqual(result.data.abilities.map((entry) => entry.ability.id), ["ability.good"]);
  assert.equal(result.data.savingThrows[0].proficient, false);
  assert.equal(result.data.temporaryHitPoints.amount, 0);
  assert.equal(result.data.hitPoints, undefined, "incoherent numeric vitals are unavailable, never guessed");
  assert.equal(result.data.spellcasting, undefined);
  assert.equal(result.data.features, undefined);
  assert.equal(result.data.inventory, undefined);
  assert.equal(result.data.wallet, undefined);
  assert.equal("unknownFutureField" in result.data, false);
});

test("T03/T08 dossier sheet identity is required, while malformed dossier enrichment stays local", async () => {
  const data = {
    version: 1,
    sheet: sheet({
      subject: ref("actor.synthetic", "Synthetic Hero"),
      abilities: [{ ability: ref("ability.good", "Good Ability"), score: 14, modifier: 2 }],
      spellcasting: [{ id: "spell.bad", name: "Bad", sourceDefinition: {}, ability: {}, preparedSpells: "bad", availableSpells: [] }],
    }),
    features: [{ malformed: true }],
    privateNarrative: "inert",
  };
  const result = await readCanonicalCharacter(reader(characterDossierContract, data));
  assert.equal(result.status, "ready");
  assert.equal(result.data.subject.id, "actor.synthetic");
  assert.equal(result.data.abilities[0].score, 14);
  assert.deepEqual(result.data.dossier.features, []);
  assert.equal(result.data.dossier.coverage, "partial");
  assert.ok(result.data.dossier.unavailableSections.includes("features"),
    "invalid optional dossier sections stay local and are explicit");
});

test("T03/T04 dossier facets retain useful peers and mark malformed sections partial", async () => {
  const valid = dossierFields();
  const data = {
    ...valid,
    version: 99,
    origin: { ...valid.origin, background: { malformed: true },
      traits: [valid.origin.traits[0], { key: "bad", label: 4 }] },
    classes: [valid.classes[0], { id: "bad", name: "Bad class", definition: {}, level: "unknown" }],
    features: [...valid.features, { malformed: true }],
    definitions: [valid.definitions[0], { malformed: true }],
    inventory: { definitions: [], contentsDepth: "unknown", mayOmitDeeperContents: true },
    levelOneRules: { ...valid.levelOneRules, entitlements: "unknown" },
    producerMetadata: { privateRuleText: "inert" },
  };
  const result = await readCanonicalCharacter(reader(characterDossierContract, data));
  assert.equal(result.status, "ready");
  assert.equal(result.data.subject.id, "actor.synthetic");
  assert.equal(result.data.dossier.coverage, "partial");
  assert.deepEqual(result.data.dossier.origin.species, valid.origin.species);
  assert.equal(result.data.dossier.origin.background, undefined);
  assert.equal(result.data.dossier.origin.traits.length, 1);
  assert.equal(result.data.dossier.classes.length, 1);
  assert.equal(result.data.dossier.features.length, 1);
  assert.equal(result.data.dossier.definitions.length, 1);
  assert.equal(result.data.dossier.inventory, undefined);
  assert.equal(result.data.dossier.levelOneRules, undefined,
    "rule/entitlement prerequisites remain strict and local");
  assert.deepEqual(result.data.dossier.unavailableSections,
    ["features", "inventory", "levelOneRules", "origin.background", "origin.traits", "definitions", "classes"].sort());
  assert.equal("producerMetadata" in result.data.dossier, false);
});

test("T03/T04 dossier rows retain identity and descriptions when optional peers are missing", async () => {
  const valid = dossierFields();
  const classDefinition = { ...valid.classes[0].definition };
  for (const field of ["canonicalName", "kind", "status", "summary", "source"]) delete classDefinition[field];
  const featureDefinition = { ...valid.features[0].definition };
  delete featureDefinition.summary;
  delete featureDefinition.source;
  const partialClass = { ...valid.classes[0], definition: classDefinition };
  delete partialClass.subclass;
  const partialFeature = { ...valid.features[0], definition: featureDefinition };
  delete partialFeature.implementation;
  const partialTrait = { ...valid.origin.traits[0] };
  for (const field of ["status", "reason", "mechanicId", "source"]) delete partialTrait[field];
  const result = await readCanonicalCharacter(reader(characterDossierContract, {
    ...valid,
    classes: [partialClass],
    features: [partialFeature],
    definitions: [featureDefinition],
    origin: { ...valid.origin, traits: [partialTrait] },
  }));
  assert.equal(result.status, "ready");
  assert.equal(result.data.dossier.classes[0].id, "class-entry.synthetic");
  assert.equal(result.data.dossier.classes[0].name, "Synthetic class");
  assert.equal(result.data.dossier.classes[0].level, 1);
  assert.equal(result.data.dossier.classes[0].definition.id, "class.synthetic");
  assert.equal(result.data.dossier.classes[0].definition.label, "Synthetic Class");
  assert.ok(result.data.dossier.classes[0].unavailableFields.includes("definition"));
  assert.ok(result.data.dossier.classes[0].unavailableFields.includes("subclass"));
  assert.equal(result.data.dossier.features[0].definition.id, "feature.synthetic");
  assert.equal(result.data.dossier.features[0].definition.label, "Synthetic Feature");
  assert.equal(result.data.dossier.features[0].definition.summary, undefined);
  assert.equal(result.data.dossier.features[0].implementation, undefined,
    "missing implementation cannot claim executable status");
  assert.ok(result.data.dossier.features[0].unavailableFields.includes("implementation"));
  assert.equal(result.data.dossier.origin.traits[0].key, "trait.synthetic");
  assert.equal(result.data.dossier.origin.traits[0].label, "Synthetic Trait");
  assert.ok(result.data.dossier.origin.traits[0].unavailableFields.includes("status"));
  assert.ok(result.data.dossier.coverage === "partial");
});

test("T06 dossier duplicate and identityless rows are all omitted, never first-winner merged", async () => {
  const valid = dossierFields();
  const duplicateClass = { ...valid.classes[0], name: "Conflicting class" };
  const duplicateFeature = { ...valid.features[0], implementation: { ...valid.features[0].implementation, status: "pending" } };
  const duplicateDefinition = { ...valid.definitions[0], summary: "Conflicting summary" };
  const duplicateTrait = { ...valid.origin.traits[0], label: "Conflicting trait" };
  const result = await readCanonicalCharacter(reader(characterDossierContract, {
    ...valid,
    classes: [valid.classes[0], duplicateClass],
    features: [valid.features[0], duplicateFeature],
    definitions: [valid.definitions[0], duplicateDefinition],
    origin: { ...valid.origin, traits: [valid.origin.traits[0], duplicateTrait] },
  }));
  assert.equal(result.status, "ready");
  assert.deepEqual(result.data.dossier.classes, []);
  assert.deepEqual(result.data.dossier.features, []);
  assert.deepEqual(result.data.dossier.definitions, []);
  assert.deepEqual(result.data.dossier.origin.traits, []);
  assert.equal(result.data.dossier.coverage, "partial");
  assert.ok(result.data.dossier.unavailableSections.includes("classes"));
  assert.ok(result.data.dossier.unavailableSections.includes("features"));
  const inherited = Object.create(valid.classes[0]);
  const inheritedResult = await readCanonicalCharacter(reader(characterDossierContract, {
    ...valid, classes: [inherited],
  }));
  assert.equal(inheritedResult.status, "ready");
  assert.deepEqual(inheritedResult.data.dossier.classes, [], "inherited row fields are not admitted");
});

test("T12 wrong actor identity remains incompatible and cannot be projected", async () => {
  const wrongActor = sheet({ subject: ref("actor.other", "Other Hero") });
  const result = await readCanonicalCharacterSheet(reader(characterSheetContract, wrongActor));
  assert.equal(result.status, "error");
  assert.equal(result.failureCategory, "incompatible-data");
});

test("T02 display admission consumes known fields across producer versions and preserves confirmed empty sections", async () => {
  const data = sheet({
    version: 17,
    abilities: [],
    savingThrows: [],
    producerMetadata: { schemaHash: "future", privateRuleText: "inert" },
  });
  const result = await readCanonicalCharacterSheet(reader(characterSheetContract, data));
  assert.equal(result.status, "ready");
  assert.equal(result.data.subject.id, "actor.synthetic");
  assert.deepEqual(result.data.abilities, []);
  assert.deepEqual(result.data.savingThrows, []);
  assert.equal(result.data.producerMetadata, undefined);
});

test("T02 dossier revision metadata does not gate its independently usable nested sheet", async () => {
  const data = { version: 99, sheet: sheet({ version: 8, origin: undefined, inventory: undefined }) };
  const result = await readCanonicalCharacter(reader(characterDossierContract, data));
  assert.equal(result.status, "ready");
  assert.equal(result.data.subject.id, "actor.synthetic");
  assert.equal(result.data.abilities[0].score, 10);
  assert.equal(result.data.origin, undefined);
});

test("portrait coverage distinguishes unavailable, confirmed empty, and denied media", () => {
  const summary = {
    id: "actor.synthetic", initials: "SH", name: "Synthetic Hero", detail: "Hero", status: "active", isCurrent: true,
    recordStatus: "Summary", sheetStatus: "provisional", inventoryStatus: "provisional",
    sheetState: { status: "empty", data: [], source: "provisional" }, inventoryState: { status: "empty", data: [], source: "provisional" },
    sheet: [], knowledge: [], backstory: [], origin: [], inventory: [],
  };
  const ready = { status: "ready", data: sheet(), failureCategory: null, diagnosticId: "ready" };
  assert.equal(projectCharacterSheet(summary, ready).portraitCoverage, "unavailable");
  assert.equal(projectCharacterSheet(summary, { ...ready, media: null }).portraitCoverage, "confirmed");
  assert.equal(projectCharacterSheet(summary, { status: "forbidden", data: null, failureCategory: "authorization", diagnosticId: "denied" }).portraitCoverage, "denied");
});

test("T03/T04 character projectors omit unavailable canonical sections without crashing the party row", () => {
  const summary = {
    id: "actor.synthetic", initials: "SH", name: "Synthetic Hero", detail: "Hero", status: "active", isCurrent: true,
    recordStatus: "Summary", sheetStatus: "provisional", inventoryStatus: "provisional",
    sheetState: { status: "ready", data: [], source: "provisional" }, inventoryState: { status: "ready", data: [], source: "provisional" },
    sheet: [], knowledge: [], backstory: [], origin: [], inventory: [],
  };
  const data = { ...sheet({ inventory: undefined, wallet: undefined }), projection: {
    stateSpaceFingerprint: "A".repeat(64), resolutionFingerprint: "B".repeat(64),
    resultFingerprint: "C".repeat(64), sourceRevisionFingerprint: "D".repeat(64),
  } };
  const projected = projectCharacterSheet(summary, { status: "ready", data, media: null, failureCategory: null, diagnosticId: "synthetic" });
  assert.equal(projected.characterSheet?.subject.id, "actor.synthetic");
  const details = projectCharacterDetails(summary, { status: "ready", data: { ...data, inventory: undefined }, media: null, failureCategory: null, diagnosticId: "synthetic" });
  assert.deepEqual(details.inventory, []);
  assert.equal(details.inventoryStatus, "unavailable");
  assert.equal(details.inventoryState.status, "error");
});
