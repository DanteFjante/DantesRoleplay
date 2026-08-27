---
id: procedure.mechanic.dnd2024.species-selection
category: ruleset.dnd2024.character.species-selection
name: Resolve an immutable D&D 2024 species selection
governs: dnd2024.selected-species; mechanic.dnd2024.species-selection.resolve
status: active
---

## Description

Owns the minimal selected-species reference and the pure character-creation planner that derives
declared Size and base Speed from one bound immutable species definition.

## Instructions

1. Bind the exact species definition as the mechanic's `species` role. Never accept a definition ID
   or profile copy from input.
2. For a one-Size profile, derive the Size from `{}`. For a two-Size profile, require exactly one
   allowed `size` choice.
3. Return canonical selected-species, Size, and base-Speed data without effects. Preserve trait and
   choice-family declarations, but list every trait without an implemented owner as unresolved.
4. `dnd2024.selected-species`, when later applied by the atomic creation root, contains only
   `speciesDefinitionId`. Size and Speed remain with their established component owners.

## Constraints

This procedure grants no species trait and performs no direct write. A plan with unresolved traits
is blocked from atomic character completion even when its Size and Speed are otherwise valid.
