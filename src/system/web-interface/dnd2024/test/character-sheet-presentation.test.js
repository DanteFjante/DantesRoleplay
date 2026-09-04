import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const source = [
  "../src/components/PartyView.tsx",
  "../src/components/character/CharacterShell.tsx",
  "../src/components/character/CharacterHero.tsx",
  "../src/components/character/CharacterOverview.tsx",
  "../src/components/character/CharacterSheet.tsx",
  "../src/components/character/CharacterSectionState.tsx",
  "../src/components/character/InventoryTree.tsx",
  "../src/components/character/WalletSummary.tsx",
].map((path) => readFileSync(new URL(path, import.meta.url), "utf8")).join("\n");
const styles = readFileSync(new URL("../src/character-page.css", import.meta.url), "utf8");

test("the character page is decomposed and renders every catalog-owned v2 section", () => {
  for (const name of ["CharacterShell", "CharacterHero", "VitalStrip", "AbilityScores", "SavesAndSkills",
    "FeatureGroups", "Spellbook", "ActionList", "InventoryTree", "WalletSummary"]) {
    assert.match(source, new RegExp(`function ${name}\\b`, "u"));
  }
  for (const property of ["identity", "origin", "classes", "abilities", "savingThrows", "skills", "hitPoints",
    "temporaryHitPoints", "armorClass", "initiative", "body", "movement", "senses", "conditions",
    "proficiencies", "features", "resources", "spellcasting", "actions", "inventory", "wallet"]) {
    assert.match(source, new RegExp(`(?:sheet|characterSheet)\\??\\.${property}\\b`, "u"));
  }
});

test("missing character sections are omitted rather than filled with made-up values", () => {
  assert.match(source, /\? \(/u);
  assert.doesNotMatch(source, /defaultArmorClass|defaultHitPoints|assumedLevel|inferredSpell/u);
  assert.doesNotMatch(source, /fetch\(|state-spaces|componentType|dnd2024\.creature/iu);
});

test("large spell and action reference sets remain bounded in the presentation", () => {
  assert.match(source, /values\.slice\(0, 12\)/u);
  assert.match(source, /values\.length - visible\.length/u);
});

test("inventory renders canonical definition details and provenance from the dossier", () => {
  assert.match(source, /dossier\?\.inventory\.definitions/u);
  assert.match(source, /definition\?\.summary/u);
  assert.match(source, /definition\?\.source/u);
  assert.doesNotMatch(source, /fixture\.legacy|legacy\.stats/u);
});

test("the character page owns responsive desktop, tablet, and mobile layouts", () => {
  assert.match(styles, /\.character-page\s*\{[^}]*min-width:\s*0/su);
  assert.match(styles, /\.character-workspace\s*\{[^}]*grid-template-columns:\s*minmax\(220px,\s*0\.25fr\)\s*minmax\(0,\s*1fr\)/su);
  for (const breakpoint of ["1100px", "820px", "520px"]) {
    assert.match(styles, new RegExp(`@media \\(max-width: ${breakpoint}\\)`, "u"));
  }
  assert.match(styles, /@media \(max-width: 820px\)[\s\S]*?\.character-workspace\s*\{\s*grid-template-columns:\s*1fr;/u);
  assert.match(styles, /@media \(max-width: 740px\)[\s\S]*?\.character-tabs\s*\{[^}]*grid-template-columns:\s*repeat\(3,\s*minmax\(0,\s*1fr\)\);[^}]*overflow-x:\s*visible;/u);
  assert.match(styles, /@media \(max-width: 520px\)[\s\S]*?\.character-wallet__totals\s*\{\s*grid-template-columns:\s*1fr;/u);
});

test("keyboard, loading, and nested-inventory semantics are explicit", () => {
  assert.match(source, /<aside aria-label="Active party roster"/u);
  assert.match(source, /<nav aria-label="Character dossier sections"/u);
  assert.match(source, /<details[\s\S]*?<summary>/u);
  assert.match(source, /aria-live="polite"/u);
  assert.match(source, /role="status"/u);
  assert.match(styles, /\.character-page summary:focus-visible/u);
  assert.match(styles, /min-height:\s*44px/u);
});
