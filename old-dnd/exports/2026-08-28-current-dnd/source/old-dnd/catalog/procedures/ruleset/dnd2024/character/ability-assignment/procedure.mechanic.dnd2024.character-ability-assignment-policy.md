---
id: procedure.mechanic.dnd2024.character-ability-assignment-policy
category: ruleset.dnd2024.character.ability-assignment
name: Validate a bound D&D 2024 ability assignment policy
governs: CH2's internal ability-assignment policy validation only
status: active
---

## Description

CH2 validates exactly six raw ability scores against an immutable, source-cited policy entity
already bound by a future character-creation root. It returns canonical scores and no effects.
It does not select a policy, create an actor, write abilities, apply origin increases, or derive a
modifier, proficiency bonus, level, grant, class, hit points, or armor class.

## Instructions

1. The root resolves one trusted policy role and passes its canonical entity id to the internal
   validator; public action input never chooses a policy id.
2. Validate the closed policy shape, registered SRD 5.2.1 source reference, score bounds, and one
   closed allocation family before inspecting submitted scores.
3. Accept only an object with exact lowercase `str`, `dex`, `con`, `int`, `wis`, and `cha`
   integer fields. For `fixed-multiset`, compare all six without labels to the declared values;
   for `point-budget`, require each score to occur in the declared ascending cost table and total
   exactly the declared budget.
4. Return canonical scores and zero effects. CH2 Slice 2 later hands that result to the existing
   `dnd2024.abilities` add-only owner; CH3 owns the Soldier increases and other origin changes.

## Constraints

- The policy is immutable content on a versioned entity, never actor state. Its component carries
  no character id, selection, class, grant, item, derived value, dice, random result, or source
  prose.
- Reject missing/extra/wrong-case/noninteger/out-of-bound/derived score fields, corrupt policy
  data, unknown source identity, blank or untrimmed locator, unsupported allocation family, and
  an assignment that does not exactly satisfy the bound policy. Every rejection returns zero
  effects and writes nothing.
- The catalog mechanic is a draft CH5 composition declaration. It must not expose a public action
  that selects policy content or records ability state outside the later root transaction.
- The permanent policy component is `dnd2024.character.ability-assignment-policy`; the initial
  Standard Array fixture is `content.dnd2024.ability-assignment.standard-array.v1`.
