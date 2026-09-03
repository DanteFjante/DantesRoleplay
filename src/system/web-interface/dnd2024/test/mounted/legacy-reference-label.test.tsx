import assert from "node:assert/strict";
import test from "node:test";

import { legacyReferenceLabel } from "../../src/data/legacy-reference-label.ts";

test("legacy reference labels hide catalog version tokens and format reference slugs", () => {
  assert.equal(legacyReferenceLabel("dnd2024.class.fighter.v1"), "Fighter");
  assert.equal(legacyReferenceLabel("dnd2024.class.fighter.v2"), "Fighter");
  assert.equal(legacyReferenceLabel("dnd2024.class.fighter.v27"), "Fighter");
  assert.equal(legacyReferenceLabel("dnd2024.skill.sleight-of-hand"), "Sleight Of Hand");
  assert.equal(legacyReferenceLabel("dnd2024.movement.walk_speed"), "Walk Speed");
});
