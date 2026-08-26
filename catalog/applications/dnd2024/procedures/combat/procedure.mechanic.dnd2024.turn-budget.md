---
id: procedure.mechanic.dnd2024.turn-budget
category: ruleset.dnd2024.core.combat.economy
name: Govern a D&D 2024 turn action-economy budget
governs: mechanic.dnd2024.turn-budget.write; mechanic.dnd2024.turn-budget.read; mechanic.dnd2024.turn-budget.spend; dnd2024.turn-budget
status: active
---

## Description

Owns a combat participant's closed Action, Bonus Action, Reaction, free-interaction, and remaining
movement state, including admission, diagnostics, start-of-turn restoration, and explicit spending.

## Instructions

1. Source: `source.dnd2024.srd-5.2.1`, `Playing the Game > Actions; Bonus Actions; Reactions;
   Interacting with Objects; Combat > Your Turn`.
2. Keep at most one complete `dnd2024.turn-budget` on a participant. Absence means the participant
   has not been admitted; it never implies that all resources are available.
3. The writer accepts exactly `record|correct`, four availability Booleans, and
   `movementRemainingFeet`; it fixes provenance and proposes exactly one add/set effect.
4. Base Speed, the active encounter participant, Conditions, action costs, and actual movement are
   separate owners. Start/advance derive only the newly active participant's reset from walk Speed
   and Exhaustion; explicit spending consumes only the selected available resource.
5. Action, Bonus Action, free interaction, and movement require the active participant. Reaction
   requires encounter membership but may be spent off turn. Shared Condition prohibitions are
   evaluated before ordinary availability.

## Constraints

Remaining movement is a whole number from 0 through 1,000. The ceiling is a repository safety bound,
not a universal SRD limit. No movement maximum, Speed, encounter, round, turn index, cost, delta,
position, event, or history is stored here.
