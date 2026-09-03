import React, { useEffect } from "react";
import { createRoot } from "react-dom/client";

import type { NamedCharacterReference, PartyMemberReadModel } from "../../src/data/hub-types";
import { PartyView } from "../../src/components/PartyView";
import "../../src/styles.css";
import "../../src/character-page.css";

const ref = (id: string, label: string): NamedCharacterReference => ({ id, label });
const definition = (id: string, label: string, kind: string) => ({
  id, label, canonicalName: label, kind, status: "active" as const,
  summary: null, source: { sourceId: "dnd2024.source.srd-5.2.1", locator: `Fixture > ${label}` },
});

const characterSheet = {
  version: 2 as const,
  subject: ref("actor.fixture.vesryn", "Vesryn Thorne"),
  identity: {
    pronouns: "they / them",
    appearance: "A road-worn half-elf in weathered green, carrying a blackwood longbow.",
    biography: "A patient guide who reads old roads and older silences.",
  },
  origin: {
    species: ref("dnd2024.species.elf", "Elf"),
    background: ref("dnd2024.background.guide", "Wilderness Guide"),
  },
  experience: { total: 6_500 },
  classes: [{
    id: "actor.fixture.vesryn.class.ranger",
    name: "Ranger 5",
    class: ref("dnd2024.class.ranger", "Ranger"),
    level: 5,
    subclass: ref("dnd2024.subclass.hunter", "Hunter"),
  }],
  level: 5,
  proficiencyBonus: 3,
  abilities: [
    ["strength", "Strength", 11, 0], ["dexterity", "Dexterity", 18, 4],
    ["constitution", "Constitution", 14, 2], ["intelligence", "Intelligence", 12, 1],
    ["wisdom", "Wisdom", 16, 3], ["charisma", "Charisma", 9, -1],
  ].map(([id, name, score, modifier]) => ({ ability: ref(`dnd2024.ability.${id}`, String(name)), score: Number(score), modifier: Number(modifier) })),
  savingThrows: [
    { ability: ref("dnd2024.ability.dexterity", "Dexterity"), proficient: true, modifier: 7 },
    { ability: ref("dnd2024.ability.wisdom", "Wisdom"), proficient: true, modifier: 6 },
  ],
  skills: [
    { skill: ref("dnd2024.skill.perception", "Perception"), ability: ref("dnd2024.ability.wisdom", "Wisdom"), proficient: true, expertise: true, modifier: 9 },
    { skill: ref("dnd2024.skill.survival", "Survival"), ability: ref("dnd2024.ability.wisdom", "Wisdom"), proficient: true, expertise: false, modifier: 6 },
    { skill: ref("dnd2024.skill.stealth", "Stealth"), ability: ref("dnd2024.ability.dexterity", "Dexterity"), proficient: true, expertise: false, modifier: 7 },
  ],
  initiative: { ability: ref("dnd2024.ability.dexterity", "Dexterity"), modifier: 4 },
  hitPoints: { current: 38, maximum: 44, maximumReduction: 0 },
  temporaryHitPoints: { amount: 5 },
  armorClass: { value: 16 },
  body: { size: ref("dnd2024.size.medium", "Medium") },
  movement: [{ kind: ref("dnd2024.movement.walk", "Walk"), numerator: 30, denominator: 1, unit: ref("dnd2024.unit.feet", "feet") }],
  senses: [{ sense: ref("dnd2024.sense.darkvision", "Darkvision"), numerator: 60, denominator: 1, unit: ref("dnd2024.unit.feet", "feet") }],
  conditions: [],
  proficiencies: [
    { proficiency: ref("dnd2024.proficiency.martial-weapons", "Martial weapons"), rank: ref("dnd2024.rank.proficient", "Proficient") },
  ],
  features: [
    { feature: ref("dnd2024.feature.favored-enemy", "Favored Enemy"), grantedBy: ref("dnd2024.class.ranger", "Ranger"), grantKind: ref("dnd2024.grant.class", "Class"), classLevel: 1 },
    { feature: ref("dnd2024.feature.extra-attack", "Extra Attack"), grantedBy: ref("dnd2024.class.ranger", "Ranger"), grantKind: ref("dnd2024.grant.class", "Class"), classLevel: 5 },
  ],
  resources: [{ id: "actor.fixture.vesryn.resource.focus", name: "Hunter's Focus", definition: ref("dnd2024.resource.focus", "Focus"), expended: 1 }],
  spellcasting: [{
    id: "actor.fixture.vesryn.spellcasting.ranger",
    name: "Ranger spells",
    sourceDefinition: ref("dnd2024.class.ranger", "Ranger"),
    ability: ref("dnd2024.ability.wisdom", "Wisdom"),
    preparedSpells: [ref("dnd2024.spell.hunters-mark", "Hunter's Mark"), ref("dnd2024.spell.goodberry", "Goodberry")],
    availableSpells: [ref("dnd2024.spell.cure-wounds", "Cure Wounds"), ref("dnd2024.spell.fog-cloud", "Fog Cloud")],
  }],
  actions: [
    { id: "actor.fixture.vesryn.action.longbow", name: "Blackwood Longbow", activities: [ref("dnd2024.activity.attack", "Ranged attack")] },
    { id: "actor.fixture.vesryn.action.shortsword", name: "Shortsword", activities: [ref("dnd2024.activity.attack", "Melee attack")] },
  ],
  inventory: {
    contentsDepth: 4 as const,
    mayOmitDeeperContents: true as const,
    items: [
      { id: "item.backpack", name: "Weathered Backpack", definition: ref("dnd2024.item.backpack", "Backpack"), quantity: 1, slot: "carried", parentItemId: null, order: 0, depth: 0, childCount: 2, deeperContentsOmitted: false, equipmentSlots: [] },
      { id: "item.pouch", name: "Leather Coin Pouch", definition: ref("dnd2024.item.pouch", "Pouch"), quantity: 1, slot: "contained", parentItemId: "item.backpack", order: 0, depth: 1, childCount: 1, deeperContentsOmitted: false, equipmentSlots: [] },
      { id: "item.coins", name: "Gold Pieces", definition: ref("dnd2024.item.gold", "Gold Piece"), quantity: 25, slot: "contained", parentItemId: "item.pouch", order: 0, depth: 2, childCount: 0, deeperContentsOmitted: false, equipmentSlots: [] },
      { id: "item.rations", name: "Trail Rations", definition: ref("dnd2024.item.rations", "Rations"), quantity: 5, slot: "contained", parentItemId: "item.backpack", order: 1, depth: 1, childCount: 0, deeperContentsOmitted: false, equipmentSlots: [] },
      { id: "item.longbow", name: "Blackwood Longbow", definition: ref("dnd2024.item.longbow", "Longbow"), quantity: 1, slot: "equipped", parentItemId: null, order: 1, depth: 0, childCount: 0, deeperContentsOmitted: false, equipmentSlots: [ref("dnd2024.slot.two-hands", "Two hands")] },
    ],
  },
  wallet: {
    coinCount: 37,
    copperValue: 2_620,
    gpCount: 25,
    denominations: [
      { denomination: ref("dnd2024.coin.gp", "Gold piece"), code: "gp" as const, count: 25, copperValuePerCoin: 100 as const, totalCopperValue: 2_500 },
      { denomination: ref("dnd2024.coin.sp", "Silver piece"), code: "sp" as const, count: 12, copperValuePerCoin: 10 as const, totalCopperValue: 120 },
    ],
  },
  dossier: {
    origin: {
      species: definition("dnd2024.species.elf", "Elf", "species"),
      background: definition("dnd2024.background.guide", "Wilderness Guide", "background"),
      traits: [{ key: "darkvision", label: "Darkvision", status: "pending" as const, reason: "application-deferred", source: null }],
    },
    classes: [{
      id: "actor.fixture.vesryn.class.ranger", name: "Ranger 5",
      definition: definition("dnd2024.class.ranger", "Ranger", "class"), level: 5,
      subclass: ref("dnd2024.subclass.hunter", "Hunter"),
    }],
    features: [
      {
        definition: definition("dnd2024.feature.favored-enemy", "Favored Enemy", "feature"),
        grantedBy: definition("dnd2024.class.ranger", "Ranger", "class"), grantKind: "class-feature", classLevel: 1,
        configurationKey: null, implementation: { status: "recorded" as const, reason: null, entitlementKey: null },
      },
      {
        definition: definition("dnd2024.feature.extra-attack", "Extra Attack", "feature"),
        grantedBy: definition("dnd2024.class.ranger", "Ranger", "class"), grantKind: "class-feature", classLevel: 5,
        configurationKey: null, implementation: { status: "recorded" as const, reason: null, entitlementKey: null },
      },
    ],
    inventory: { definitions: [], contentsDepth: 4 as const, mayOmitDeeperContents: true as const },
    definitions: [],
    provenance: {
      sheetQueryId: "dnd2024.query.character-sheet-v2" as const,
      sheetProjectionId: "dnd2024.mechanic.character-sheet-v2.project" as const,
      dossierProjectionId: "dnd2024.mechanic.character-dossier-v1.project" as const,
      definitionCount: 0, inventoryDepth: 4 as const, ruleTextPolicy: "canonical-only" as const,
    },
  },
};

const party: PartyMemberReadModel[] = [{
  id: "actor.fixture.vesryn",
  initials: "VT",
  name: "Vesryn Thorne",
  detail: "Elf Ranger 5 · Hunter",
  status: "active",
  isCurrent: true,
  recordStatus: "ready",
  sheetStatus: "canonical",
  inventoryStatus: "canonical",
  sheetState: { status: "ready", source: "canonical", data: [] },
  inventoryState: { status: "ready", source: "canonical", data: [] },
  sheet: [],
  knowledge: [{ id: "knowledge.road", kind: "Location", stance: "known", text: "The old north road floods after three days of rain." }],
  backstory: [{ id: "backstory.calling", kind: "Calling", title: "Roadwarden", detail: "Keeps travelers alive where maps stop being honest." }],
  origin: [{ id: "origin.borderlands", kind: "Homeland", title: "The Borderlands", detail: "Raised among watchfires and wind-bent pines." }],
  inventory: [],
  characterSheet,
}];

function Fixture() {
  useEffect(() => {
    const requested = new URLSearchParams(window.location.search).get("section");
    const label = requested === "inventory" ? "Inventory" : requested === "character" ? "Character" : null;
    requestAnimationFrame(() => {
      if (label) {
        const target = [...document.querySelectorAll<HTMLButtonElement>("button")].find((candidate) => candidate.textContent?.trim() === label);
        target?.click();
      }
      requestAnimationFrame(() => {
        document.documentElement.dataset.horizontalOverflow = String(
          document.documentElement.scrollWidth > document.documentElement.clientWidth,
        );
        document.documentElement.dataset.layoutAudit = JSON.stringify({
          viewport: { width: document.documentElement.clientWidth, height: document.documentElement.clientHeight },
          elements: Object.fromEntries(["main", ".character-page", ".character-workspace", ".character-dossier", ".character-overview__lead"].map((selector) => {
            const element = document.querySelector<HTMLElement>(selector);
            const rect = element?.getBoundingClientRect();
            return [selector, rect ? { left: Math.round(rect.left), right: Math.round(rect.right), width: Math.round(rect.width) } : null];
          })),
        });
      });
    });
  }, []);
  return <main style={{ maxWidth: 1480, margin: "0 auto", padding: "28px clamp(12px, 3vw, 44px)" }}><PartyView party={party} /></main>;
}

createRoot(document.getElementById("root")!).render(<Fixture />);
