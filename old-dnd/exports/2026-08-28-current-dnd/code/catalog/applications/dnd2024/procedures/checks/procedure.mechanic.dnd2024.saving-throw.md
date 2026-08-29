---
id: procedure.mechanic.dnd2024.saving-throw
category: ruleset.dnd2024.core.gameplay.saving-throws.fixed-dc
name: Resolve a fixed-DC saving throw
governs: mechanic.dnd2024.saving-throw; an action resolving an imposed fixed-DC character save
status: active
---

## Description

Resolves one D&D 2024 fixed-DC saving throw from authoritative ability, level, and save-proficiency state.

## Instructions

1. Accept an exact ability ID and integer DC, with optional explicit D20 roll circumstances or voluntary failure.
2. For a rolled save, apply the ability modifier and the character's level-derived Proficiency Bonus once only if that ability's save is proficient; use 7A3's Advantage/Disadvantage convention.
3. For voluntary failure, return a failed zero-roll result. Report no consequence and propose no effects, events, or notifications.

## Constraints

- The cause determines DC and consequences; this procedure does neither.
- Natural 20 and natural 1 do not override saving-throw totals.
- Voluntary failure cannot be combined with nonempty circumstances.
- Persistent conditions, class grants, monster CR, and caller-supplied derived values are outside this slice.
