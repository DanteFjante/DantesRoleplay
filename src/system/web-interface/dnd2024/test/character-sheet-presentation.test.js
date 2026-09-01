import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const component = readFileSync(new URL("../src/components/PartyView.tsx", import.meta.url), "utf8");

test("the character sheet renders every available catalog-owned section", () => {
  assert.match(component, /sheet\.identity/u);
  assert.match(component, /sheet\.experience/u);
  assert.match(component, /sheet\.origin/u);
  assert.match(component, /sheet\.classes/u);
  assert.match(component, /sheet\.abilities/u);
  assert.match(component, /sheet\.savingThrows/u);
  assert.match(component, /sheet\.skills/u);
  assert.match(component, /sheet\.hitPoints/u);
  assert.match(component, /sheet\.temporaryHitPoints/u);
  assert.match(component, /sheet\.armorClass/u);
  assert.match(component, /sheet\.initiative/u);
  assert.match(component, /sheet\.body/u);
  assert.match(component, /sheet\.movement/u);
  assert.match(component, /sheet\.senses/u);
  assert.match(component, /sheet\.conditions/u);
  assert.match(component, /sheet\.proficiencies/u);
  assert.match(component, /sheet\.features/u);
  assert.match(component, /sheet\.resources/u);
  assert.match(component, /sheet\.spellcasting/u);
  assert.match(component, /sheet\.actions/u);
});

test("missing character sections are omitted rather than filled with made-up values", () => {
  assert.match(component, /\? \(/u);
  assert.doesNotMatch(component, /defaultArmorClass|defaultHitPoints|assumedLevel|inferredSpell/u);
  assert.doesNotMatch(component, /fetch\(|state-spaces|componentType|dnd2024\.creature/iu);
});

test("large spell and action reference sets remain bounded in the presentation", () => {
  assert.match(component, /values\.slice\(0, 12\)/u);
  assert.match(component, /values\.length - visible\.length/u);
});
