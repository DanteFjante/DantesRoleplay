---
id: dnd2024.procedure.mechanic.dice
category: ruleset.dnd2024.core.gameplay.dice
name: Roll dice
governs: commit(kind: "mechanic") for the core D&D 2024 dice-roll mechanic and commit(kind: "action") when a participant asks to roll dice.
status: active
createdBy: "llm"
changeNote: "Creates the core reusable D\u0026D 2024 dice mechanic under ruleset.dnd2024.core.gameplay.dice."
---

## Description
Provides a simple, seeded dice roll for D&D 2024 actions without changing world state.

## Instructions
1. Accept an action intent that clearly asks to roll dice, such as "roll a d20" or "roll 2d6+3".
2. Read count, sides, and modifier from action input when supplied; default to count 1, sides 20, and modifier 0.
3. Require positive integer count and sides, and an integer modifier.
4. Use the seeded random helper for every die; never use Math.random().
5. Return the individual rolls, modifier, and total in narration and structured data.
6. Produce no world effects; this mechanic only answers a roll.
7. Verify the mechanic with a representative action after committing it.

## Constraints
- This mechanic must not modify world state.
- Every die result must be generated with ctx.randomInt(1, sides).
- The mechanic must be scoped to dnd2024-srd-5.2.1 and categorized under ruleset.dnd2024.core.
- Existing ids are permanent; revise rather than rename or repurpose.
