---
id: procedure.mechanic.dnd2024.character-abilities
category: ruleset.dnd2024.character.creation.abilities
name: Resolve D&D 2024 character-creation abilities
governs: dnd2024.character.ability-assignment-policy; dnd2024.background.ability-increase-options; mechanic.dnd2024.character-abilities.resolve
status: active
---

## Description

Validates one immutable ability-generation policy and one exact active background declaration,
then derives the final six raw ability scores for a future character-creation root.

## Instructions

1. Bind the policy and background as roles; callers never submit their IDs or component payloads.
2. Accept exactly six base scores and one background increase selection.
3. For a fixed multiset, use each declared value exactly once. For point cost, accept only declared
   scores whose costs spend the exact declared budget.
4. Apply only a background-declared `+2/+1` on different eligible abilities or `+1` to all three
   eligible abilities, and never raise a score above 20.
5. Return canonical base scores, selected increases, and final scores with no effects. A later
   creation root may add the final scores through the existing `dnd2024.abilities` owner.

Source: `source.dnd2024.srd-5.2.1`, *Character Creation > Step 3: Ability Scores* (PDF p. 21) and
*Character Origins > Character Backgrounds > Parts of a Background* (PDF p. 83), CC BY 4.0.

## Constraints

- Policy and background declarations are immutable catalog content, never actor state.
- Do not store modifiers, final scores on the policy/background, character selections, feats,
  proficiencies, grants, or rules prose in these components.
- Missing, extra, derived, malformed, source-drifted, ineligible, or over-cap input fails before an
  output and never changes state.
- Random score generation, recording, origin completion, and actor creation are separate leaves.
